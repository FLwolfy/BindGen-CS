using System.Threading.Tasks;
using Xunit;

namespace BGCS.Configuration.Tests;

public sealed class GroupedConfigurationSelectionEntryTests : GroupedConfigurationEntryTestBase
{
    [Theory]
    [InlineData("AllowedConstants", "config")]
    [InlineData("AllowedDelegates", "config")]
    [InlineData("AllowedEnums", "config")]
    [InlineData("AllowedFunctions", "config")]
    [InlineData("AllowedTypes", "config")]
    [InlineData("IgnoredConstants", "config")]
    [InlineData("IgnoredConstants", "config.alt")]
    [InlineData("IgnoredDelegates", "config")]
    [InlineData("IgnoredDelegates", "config.alt")]
    [InlineData("IgnoredEnums", "config")]
    [InlineData("IgnoredEnums", "config.alt")]
    [InlineData("IgnoredFunctions", "config")]
    [InlineData("IgnoredFunctions", "config.alt")]
    [InlineData("IgnoredTypes", "config")]
    [InlineData("IgnoredTypes", "config.alt")]
    [InlineData("GenerateConstants", "config")]
    [InlineData("GenerateConstants", "config.true")]
    [InlineData("GenerateDelegates", "config")]
    [InlineData("GenerateDelegates", "config.true")]
    [InlineData("GenerateEnums", "config")]
    [InlineData("GenerateEnums", "config.true")]
    [InlineData("GenerateFunctions", "config")]
    [InlineData("GenerateFunctions", "config.true")]
    [InlineData("GenerateTypes", "config")]
    [InlineData("GenerateTypes", "config.alt")]
    public Task IndependentSelectionPreservesTheExactPublicApi(
        string fixture,
        string variant
    )
        => VerifyAsync(fixture, variant);
}
