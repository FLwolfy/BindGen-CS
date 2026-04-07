using Xunit;

namespace BGCS.Configuration.Tests;

public class EnumItemNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void EnumItemNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
