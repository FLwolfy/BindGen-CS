using Xunit;

namespace BGCS.Configuration.Tests;

public class HandleNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void HandleNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
