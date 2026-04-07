using Xunit;

namespace BGCS.Configuration.Tests;

public class DefinesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void Defines_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
