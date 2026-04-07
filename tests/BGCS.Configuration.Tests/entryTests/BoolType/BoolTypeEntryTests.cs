using Xunit;

namespace BGCS.Configuration.Tests;

public class BoolTypeEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void BoolType_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
