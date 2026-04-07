using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredEnums_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
