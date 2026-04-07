using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateDelegatesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateDelegates_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
