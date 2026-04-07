using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateEnums_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
