using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Templates.Core.Gen
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum DotnetFramework
    {
        [EnumMember(Value = ".NET Core 3.1")]
        DotNetCore31,

        [EnumMember(Value = ".NET 5.0")]
        DotNet50,
    }
}
