﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.Templates.Core;
using Microsoft.Templates.Core.Composition;
using Microsoft.Templates.Core.Resources;
using Microsoft.Templates.Core.Templates;
using Newtonsoft.Json;

namespace Microsoft.Templates.Core.Gen
{
    public class GenComposer
    {
        public static IEnumerable<GenInfo> Compose(UserSelection userSelection)
        {
            var genQueue = new List<GenInfo>();

            if (string.IsNullOrEmpty(userSelection.Context.ProjectType) || string.IsNullOrEmpty(userSelection.Context.FrontEndFramework))
            {
                return genQueue;
            }

            AddProject(userSelection, genQueue);
            AddTemplates(userSelection.Pages, genQueue, userSelection, false);
            AddTemplates(userSelection.Features, genQueue, userSelection, false);
            AddTemplates(userSelection.Services, genQueue, userSelection, false);
            AddTemplates(userSelection.Testing, genQueue, userSelection, false);

            genQueue = AddInCompositionTemplates(genQueue, userSelection, false);

            return genQueue;
        }

        public static IEnumerable<TemplateLicense> GetAllLicences(UserSelection userSelection)
        {
            return Compose(userSelection)
                    .SelectMany(s => s.Template.GetLicenses())
                    .Distinct(new TemplateLicenseEqualityComparer())
                    .ToList();
        }

        public static IEnumerable<GenInfo> ComposeNewItem(UserSelection userSelection)
        {
            var genQueue = new List<GenInfo>();

            if (string.IsNullOrEmpty(userSelection.Context.ProjectType) || string.IsNullOrEmpty(userSelection.Context.FrontEndFramework))
            {
                return genQueue;
            }

            AddTemplates(userSelection.Pages, genQueue, userSelection, true);
            AddTemplates(userSelection.Features, genQueue, userSelection, true);
            AddTemplates(userSelection.Services, genQueue, userSelection, true);
            AddTemplates(userSelection.Testing, genQueue, userSelection, true);

            genQueue = AddInCompositionTemplates(genQueue, userSelection, true);

            return genQueue;
        }

        public static IEnumerable<string> GetAllRequiredVersions(UserSelection userSelection)
        {
            return Compose(userSelection)
                    .SelectMany(s => s.Template.GetRequiredVersions())
                    .Distinct()
                    .ToList();
        }

        private static void AddProject(UserSelection userSelection, List<GenInfo> genQueue)
        {
            var projectTemplates = GenContext.ToolBox.Repo
                .GetTemplates(TemplateType.Project, userSelection.Context)
                .OrderBy(projectTemplate => projectTemplate.GetCompositionOrder());

            foreach (var projectTemplate in projectTemplates)
            {
                var genProject = CreateGenInfo(GenContext.Current.ProjectName, projectTemplate, genQueue, userSelection, false);

                AddCasingParams(GenContext.Current.ProjectName, projectTemplate, genProject);
            }
        }

        private static void AddTemplates(IEnumerable<UserSelectionItem> selectedTemplates, List<GenInfo> genQueue, UserSelection userSelection, bool newItemGeneration)
        {
            foreach (var selectedTemplate in selectedTemplates)
            {
                if (!genQueue.Any(t => t.Name == selectedTemplate.Name && t.Template.Identity == selectedTemplate.TemplateId))
                {
                    var template = GenContext.ToolBox.Repo.Find(t => t.Identity == selectedTemplate.TemplateId);
                    AddRequiredTemplates(template, genQueue, userSelection, newItemGeneration);
                    AddDependencyTemplates(template, genQueue, userSelection, newItemGeneration);
                    var genInfo = CreateGenInfo(selectedTemplate.Name, template, genQueue, userSelection, newItemGeneration);

                    foreach (var dependency in genInfo?.Template.GetDependencyList())
                    {
                        if (genInfo.Template.Parameters.Any(p => p.Name == dependency))
                        {
                            var dependencyName = genQueue.FirstOrDefault(t => t.Template.Identity == dependency).Name;
                            genInfo.Parameters.Add(dependency, dependencyName);
                        }
                    }

                    AddCasingParams(selectedTemplate.Name, template, genInfo);
                }
            }
        }

        private static void AddDependencyTemplates(ITemplateInfo template, List<GenInfo> genQueue, UserSelection userSelection, bool newItemGeneration)
        {
            var dependencies = GenContext.ToolBox.Repo.GetDependencies(template, userSelection.Context, new List<ITemplateInfo>());

            foreach (var dependencyItem in dependencies)
            {
                var dependencyTemplate = userSelection.Items.FirstOrDefault(f => f.TemplateId == dependencyItem.Identity);

                if (dependencyTemplate != null)
                {
                    if (!genQueue.Any(t => t.Name == dependencyTemplate.Name && t.Template.Identity == dependencyTemplate.TemplateId))
                    {
                        var dependencyTemplateInfo = GenContext.ToolBox.Repo.Find(t => t.Identity == dependencyTemplate.TemplateId);
                        var depGenInfo = CreateGenInfo(dependencyTemplate.Name, dependencyTemplateInfo, genQueue, userSelection, newItemGeneration);

                        AddCasingParams(dependencyTemplate.Name, dependencyTemplateInfo, depGenInfo);
                    }
                }
                else
                {
                    LogOrAlertException(string.Format(StringRes.ErrorDependencyMissing, dependencyItem.Identity));
                }
            }
        }

        private static void AddRequiredTemplates(ITemplateInfo template, List<GenInfo> genQueue, UserSelection userSelection, bool newItemGeneration)
        {
            var requirements = GenContext.ToolBox.Repo.GetRequirements(template, userSelection.Context);

            if (requirements.Count() > 0)
            {
                var requirementTemplate = userSelection.Items.FirstOrDefault(f => requirements.Select(r => r.Identity).Contains(f.TemplateId));

                if (requirementTemplate != null)
                {
                    if (!genQueue.Any(t => t.Name == requirementTemplate.Name && t.Template.Identity == requirementTemplate.TemplateId))
                    {
                        var requirementTemplateInfo = GenContext.ToolBox.Repo.Find(t => t.Identity == requirementTemplate.TemplateId);
                        var depGenInfo = CreateGenInfo(requirementTemplate.Name, requirementTemplateInfo, genQueue, userSelection, newItemGeneration);

                        AddCasingParams(requirementTemplate.Name, requirementTemplateInfo, depGenInfo);
                    }
                }
            }
        }

        private static List<GenInfo> AddInCompositionTemplates(List<GenInfo> genQueue, UserSelection userSelection, bool newItemGeneration)
        {
            var compositionCatalog = GetCompositionCatalog(userSelection.Context.Platform).ToList();

            var context = new QueryablePropertyDictionary
            {
                new QueryableProperty("projecttype", userSelection.Context.ProjectType),
                new QueryableProperty("page", string.Join("|", userSelection.Pages.Select(p => p.TemplateId))),
                new QueryableProperty("feature", string.Join("|", userSelection.Features.Select(p => p.TemplateId))),
                new QueryableProperty("service", string.Join("|", userSelection.Services.Select(p => p.TemplateId))),
                new QueryableProperty("testing", string.Join("|", userSelection.Testing.Select(p => p.TemplateId))),
            };

            if (userSelection.Context.TargetDotnetFramework.HasValue) {
                string dotnet = userSelection.Context.TargetDotnetFramework.Value.ToString();
                context.Add(new QueryableProperty("dotnet", dotnet));
            }

            if (!string.IsNullOrEmpty(userSelection.Context.FrontEndFramework))
            {
                context.Add(new QueryableProperty("frontendframework", userSelection.Context.FrontEndFramework));
            }

            if (!string.IsNullOrEmpty(userSelection.Context.BackEndFramework))
            {
                context.Add(new QueryableProperty("backendframework", userSelection.Context.BackEndFramework));
            }

            foreach (var property in userSelection.Context.PropertyBag)
            {
                context.Add(new QueryableProperty(property.Key.ToLowerInvariant(), property.Value));
            }

            var combinedQueue = new List<GenInfo>();

            foreach (var genItem in genQueue)
            {
                combinedQueue.Add(genItem);
                var compositionQueue = new List<GenInfo>();

                context.AddOrUpdate(new QueryableProperty("ishomepage", (genItem.Name == userSelection.HomeName).ToString().ToLowerInvariant()));

                foreach (var compositionItem in compositionCatalog)
                {
                    if (compositionItem.Template.GetLanguage() == userSelection.Context.Language
                     && compositionItem.Query.Match(genItem.Template, context))
                    {
                        AddTemplate(genItem, compositionQueue, compositionItem.Template, userSelection, newItemGeneration);
                    }
                }

                combinedQueue.AddRange(compositionQueue.OrderBy(g => g.Template.GetCompositionOrder()));
            }

            return combinedQueue;
        }

        private static IEnumerable<CompositionInfo> GetCompositionCatalog(string platform)
        {
            return GenContext.ToolBox.Repo
                                        .Get(t => t.GetTemplateType() == TemplateType.Composition && t.GetPlatform() == platform)
                                        .Select(t => new CompositionInfo() { Query = CompositionQuery.Parse(t.GetCompositionFilter()), Template = t })
                                        .ToList();
        }

        private static void AddTemplate(GenInfo mainGenInfo, List<GenInfo> queue, ITemplateInfo targetTemplate, UserSelection userSelection, bool newItemGeneration)
        {
            if (targetTemplate != null)
            {
                foreach (var export in targetTemplate.GetExports())
                {
                    mainGenInfo.Parameters.Add(export.Key, export.Value);
                }

                var genInfo = CreateGenInfo(mainGenInfo.Name, targetTemplate, queue, userSelection, newItemGeneration);

                foreach (var param in mainGenInfo.Parameters)
                {
                    if (!genInfo.Parameters.ContainsKey(param.Key))
                    {
                        genInfo.Parameters.Add(param.Key, param.Value);
                    }
                }

                AddCasingParams(mainGenInfo.Name, targetTemplate, genInfo);
            }
        }

        private static void LogOrAlertException(string message)
        {
#if DEBUG
            throw new GenException(message);
#else
            Core.Diagnostics.AppHealth.Current.Error.TrackAsync(message).FireAndForget();
#endif
        }

        private static GenInfo CreateGenInfo(string name, ITemplateInfo template, List<GenInfo> queue, UserSelection userSelection, bool newItemGeneration)
        {
            var genInfo = new GenInfo(name, template);

            queue.Add(genInfo);

            AddDefaultParams(genInfo, userSelection, newItemGeneration);

            if (template.GetTemplateOutputType() == TemplateOutputType.Project)
            {
                AddProjectParams(genInfo, userSelection);
            }

            return genInfo;
        }

        private static void AddDefaultParams(GenInfo genInfo, UserSelection userSelection, bool newItemGeneration)
        {
            var ns = string.Empty;

            if (newItemGeneration)
            {
                ns = GenContext.ToolBox.Shell.Project.GetActiveProjectNamespace();
            }

            if (string.IsNullOrEmpty(ns))
            {
                ns = GenContext.Current.SafeProjectName;
            }

            genInfo?.Parameters.Add(GenParams.RootNamespace, ns);
            genInfo?.Parameters.Add(GenParams.ProjectName, GenContext.Current.ProjectName);
            genInfo?.Parameters.Add(GenParams.HomePageName, userSelection.HomeName);

            // TODO: Again, these make a lot more sense to be a 3rd party property bag,
            // but the userSelection.Context.PropertyBag has custom prefix "wts.generation.".
            // Also maybe validation?
            genInfo?.Parameters.Add(GenParams.Theme, userSelection.Context.Theme);
            genInfo?.Parameters.Add(GenParams.LastKnownGood, userSelection.Context.LastKnownGood ? "true" : "false");
        }

        private static void AddProjectParams(GenInfo projectGenInfo, UserSelection userSelection)
        {
            projectGenInfo?.Parameters.Add(GenParams.Username, Environment.UserName);
            projectGenInfo?.Parameters.Add(GenParams.WizardVersion, string.Concat("v", GenContext.ToolBox.WizardVersion));
            projectGenInfo?.Parameters.Add(GenParams.TemplatesVersion, string.Concat("v", GenContext.ToolBox.TemplatesVersion));
            projectGenInfo?.Parameters.Add(GenParams.ProjectType, userSelection.Context.ProjectType);
            projectGenInfo?.Parameters.Add(GenParams.FrontEndFramework, userSelection.Context.FrontEndFramework ?? string.Empty);
            projectGenInfo?.Parameters.Add(GenParams.BackEndFramework, userSelection.Context.BackEndFramework ?? string.Empty);
            projectGenInfo?.Parameters.Add(GenParams.Platform, userSelection.Context.Platform);

            // TODO: Again, these make a lot more sense to be a 3rd party property bag,
            // but the userSelection.Context.PropertyBag has custom prefix "wts.generation.".
            // Also maybe validation?
            projectGenInfo?.Parameters.Add(GenParams.IsTrial, PrimitiveScriptValue(userSelection.Context.IsTrial));
            projectGenInfo?.Parameters.Add(GenParams.TargetDotnetFramework, PrimitiveScriptValue(userSelection.Context.TargetDotnetFramework));

            foreach (var property in userSelection.Context.PropertyBag)
            {
                projectGenInfo?.Parameters.Add($"{GenParams.GenerationPropertiesPrefix}.{property.Key.ToLowerInvariant()}", property.Value);
            }
        }

        /// <summary>
        /// Converts simple value types to strings.
        /// Booleans are converted to JavaScript-like lowercase 'true' and 'false'.
        /// Enums are also converted, taking into consideration their <seealso cref="System.Runtime.Serialization.EnumMemberAttribute.Value"/>.
        /// </summary>
        private static string PrimitiveScriptValue<T>(T obj) => JsonConvert.SerializeObject(obj).Trim('"');

        private static void AddCasingParams(string name, ITemplateInfo template, GenInfo genInfo)
        {
            foreach (var textCasing in template.GetTextCasings())
            {
                var value = textCasing.Key == "sourceName"
                    ? name
                    : genInfo.Parameters.SafeGet($"wts.{textCasing.Key}");

                if (!string.IsNullOrEmpty(value))
                {
                    if (!genInfo.Parameters.ContainsKey(textCasing.ParameterName))
                    {
                        genInfo.Parameters.Add(textCasing.ParameterName, textCasing.Transform(value));
                    }
                }
            }
        }
    }
}
