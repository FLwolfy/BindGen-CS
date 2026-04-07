using Xunit;

namespace BGCS.Configuration.Tests;

public class CustomEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void CustomEnums_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
