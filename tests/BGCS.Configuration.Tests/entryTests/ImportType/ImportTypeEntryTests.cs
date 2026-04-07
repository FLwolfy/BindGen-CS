using Xunit;

namespace BGCS.Configuration.Tests;

public class ImportTypeEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ImportType_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
