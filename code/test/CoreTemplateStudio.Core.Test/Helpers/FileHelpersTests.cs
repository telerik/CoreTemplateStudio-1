﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.Templates.Core.Helpers;
using Xunit;

namespace Microsoft.Templates.Core.Test.Helpers
{
    [Trait("ExecutionSet", "Minimum")]
    public class FileHelpersTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("   ")]
        [InlineData(null)]
        public void GetFileContent_EmptyFilePath_ShouldReturnEmpty(string sourceFile)
        {
            var actual = FileHelper.GetFileContent(sourceFile);

            Assert.Empty(actual);
        }

        [Theory]
        [InlineData("TestNotExisting.csproj")]
        public void GetFileContent_FileNotExists_ShouldReturnEmpty(string projectFile)
        {
            var sourceFile = Path.Combine(Environment.CurrentDirectory, $"TestData\\TestProject\\{projectFile}");

            var actual = FileHelper.GetFileContent(sourceFile);

            Assert.Empty(actual);
        }

        [Fact]
        public void GetFileContent_Errors_ShouldReturnEmpty()
        {
            var sourceFile = Path.Combine(Environment.CurrentDirectory, $"TestData\\TestProject\\");

            var actual = FileHelper.GetFileContent(sourceFile);

            Assert.Empty(actual);
        }

        [Fact]
        public void GetFileContent_FileExists_ShouldReturnsCorrectly()
        {
            var projectName = "Test";
            var projectFile = $"{projectName}.csproj";
            var sourceFile = Path.Combine(Environment.CurrentDirectory, $"TestData\\TestProject\\{projectFile}");

            var actual = FileHelper.GetFileContent(sourceFile);

            Assert.NotEmpty(actual);
        }
    }
}
