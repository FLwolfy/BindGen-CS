using System.Threading.Tasks;
using Xunit;

namespace BGCS.Configuration.Tests;

public sealed class GroupedConfigurationEntryTests : GroupedConfigurationEntryTestBase
{
    [Theory]
    [InlineData("Namespace", "config")]
    [InlineData("Namespace", "config.alt")]
    [InlineData("ApiName", "config")]
    [InlineData("LibName", "config")]
    [InlineData("LibName", "config.alt")]
    [InlineData("ImportType", "config")]
    [InlineData("ImportType", "config.alt")]
    [InlineData("ImportType", "config.library")]
    [InlineData("MergeGeneratedFilesToSingleFile", "config")]
    [InlineData("MergeGeneratedFilesToSingleFile", "config.alt")]
    [InlineData("GenerateRuntimeSource", "config")]
    [InlineData("GenerateRuntimeSource", "config.alt")]
    [InlineData("RuntimeNamespace", "config")]
    [InlineData("RuntimeNamespace", "config.alt")]
    [InlineData("Defines", "config")]
    [InlineData("IncludeFolders", "config")]
    [InlineData("IncludeFolders", "config.alt")]
    [InlineData("SystemIncludeFolders", "config")]
    [InlineData("SystemIncludeFolders", "config.alt")]
    [InlineData("Usings", "config")]
    [InlineData("Usings", "config.alt")]
    [InlineData("FunctionNamingConvention", "config")]
    [InlineData("FunctionNamingConvention", "config.pascal")]
    [InlineData("ConstantNamingConvention", "config")]
    [InlineData("ConstantNamingConvention", "config.camel")]
    [InlineData("ConstantNamingConvention", "config.screaming")]
    [InlineData("TypeNamingConvention", "config")]
    [InlineData("TypeNamingConvention", "config.alt")]
    [InlineData("EnumNamingConvention", "config")]
    [InlineData("EnumNamingConvention", "config.pascal")]
    [InlineData("EnumItemNamingConvention", "config")]
    [InlineData("EnumItemNamingConvention", "config.pascal")]
    [InlineData("ParameterNamingConvention", "config")]
    [InlineData("ParameterNamingConvention", "config.alt")]
    public Task GroupedConfigurationPreservesItsDeclaredBehavior(string fixture, string variant)
        => VerifyAsync(fixture, variant);
}
