using System.Threading.Tasks;
using Xunit;

namespace BGCS.Configuration.Tests;

public sealed class GroupedConfigurationManagedEnumEntryTests : GroupedConfigurationEntryTestBase
{
    [Theory]
    [InlineData("config")]
    [InlineData("config.comment-plain")]
    [InlineData("config.comment-preformatted")]
    public Task ManagedOnlyEnumConfigurationPreservesValuesAndInertDocumentation(string variant)
        => VerifyAsync("CustomEnums", variant);
}
