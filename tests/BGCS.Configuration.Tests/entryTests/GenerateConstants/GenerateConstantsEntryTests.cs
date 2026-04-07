using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateConstantsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateConstants_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
