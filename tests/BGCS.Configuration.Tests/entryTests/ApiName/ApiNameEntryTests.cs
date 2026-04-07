using Xunit;

namespace BGCS.Configuration.Tests;

public class ApiNameEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ApiName_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
