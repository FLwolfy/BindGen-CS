using Xunit;

namespace BGCS.Configuration.Tests;

public class OneFilePerTypeEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void OneFilePerType_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
