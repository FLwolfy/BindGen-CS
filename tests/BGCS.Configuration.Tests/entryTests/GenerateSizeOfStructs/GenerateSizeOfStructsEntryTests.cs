using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateSizeOfStructsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateSizeOfStructs_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
