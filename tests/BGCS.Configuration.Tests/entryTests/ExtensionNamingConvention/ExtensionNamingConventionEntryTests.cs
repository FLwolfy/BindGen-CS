using Xunit;

namespace BGCS.Configuration.Tests;

public class ExtensionNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ExtensionNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
