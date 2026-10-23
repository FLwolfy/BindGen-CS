using System.Threading.Tasks;
using Xunit;

namespace BGCS.Configuration.Tests;

public sealed class GroupedConfigurationConvenienceEntryTests : GroupedConfigurationEntryTestBase
{
    [Theory]
    [InlineData("BaseConfig", "config")]
    [InlineData("IgnoredParts", "config")]
    [InlineData("IgnoredParts", "config.alt")]
    [InlineData("KnownDefaultValueNames", "config")]
    [InlineData("KnownDefaultValueNames", "config.alt")]
    [InlineData("GenerateConstructorsForStructs", "config")]
    [InlineData("GenerateConstructorsForStructs", "config.alt")]
    [InlineData("GenerateSizeOfStructs", "config")]
    [InlineData("GenerateSizeOfStructs", "config.alt")]
    public Task CompositionNamesDefaultsAndRecordConveniencesRetainTheirMeaning(
        string fixture,
        string variant
    )
        => VerifyAsync(fixture, variant);
}
