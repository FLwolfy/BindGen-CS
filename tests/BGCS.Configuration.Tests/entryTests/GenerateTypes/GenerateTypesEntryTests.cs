using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateTypesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateTypes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
