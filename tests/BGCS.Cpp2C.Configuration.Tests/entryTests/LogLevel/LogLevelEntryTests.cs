using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class LogLevelEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void LogLevel_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void LogLevel_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
    }
}
