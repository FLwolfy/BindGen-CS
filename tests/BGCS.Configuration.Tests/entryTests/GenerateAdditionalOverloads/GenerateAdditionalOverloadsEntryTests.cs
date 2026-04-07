using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateAdditionalOverloadsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateAdditionalOverloads_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
