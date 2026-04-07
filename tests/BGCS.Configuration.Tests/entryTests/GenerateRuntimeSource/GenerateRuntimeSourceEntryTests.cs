using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateRuntimeSourceEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateRuntimeSource_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
