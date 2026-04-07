using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredPartsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredParts_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
