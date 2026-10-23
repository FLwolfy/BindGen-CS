using System.Threading.Tasks;
using Xunit;

namespace BGCS.Configuration.Tests;

public sealed class GroupedConfigurationPresentationEntryTests : GroupedConfigurationEntryTestBase
{
    [Theory]
    [InlineData("KnownConstantNames", "config")]
    [InlineData("KnownConstantNames", "config.alt")]
    [InlineData("KnownEnumValueNames", "config")]
    [InlineData("KnownEnumValueNames", "config.alt")]
    [InlineData("KnownEnumPrefixes", "config")]
    [InlineData("KnownEnumPrefixes", "config.alt")]
    [InlineData("MemberNamingConvention", "config")]
    [InlineData("MemberNamingConvention", "config.alt")]
    [InlineData("DelegateNamingConvention", "config")]
    [InlineData("AdditionalArguments", "config")]
    [InlineData("AdditionalArguments", "config.no-args")]
    [InlineData("GeneratePlaceholderComments", "config")]
    [InlineData("GeneratePlaceholderComments", "config.true")]
    public Task NamesArgumentsAndDocumentationPreserveTheirExactBehavior(
        string fixture,
        string variant
    )
        => VerifyAsync(fixture, variant);
}
