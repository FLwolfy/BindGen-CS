using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateConstructorsForStructsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateConstructorsForStructs_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
