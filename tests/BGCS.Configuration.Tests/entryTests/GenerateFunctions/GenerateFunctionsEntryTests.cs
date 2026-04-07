using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateFunctionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateFunctions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
