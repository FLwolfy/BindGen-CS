using Xunit;

namespace BGCS.Configuration.Tests;

public class LibNameEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void LibName_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
