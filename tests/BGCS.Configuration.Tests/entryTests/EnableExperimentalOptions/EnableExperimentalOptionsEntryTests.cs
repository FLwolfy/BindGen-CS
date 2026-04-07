using Xunit;

namespace BGCS.Configuration.Tests;

public class EnableExperimentalOptionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void EnableExperimentalOptions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
