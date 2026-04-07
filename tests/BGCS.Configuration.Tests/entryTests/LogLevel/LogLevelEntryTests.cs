using Xunit;

namespace BGCS.Configuration.Tests;

public class LogLevelEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void LogLevel_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
