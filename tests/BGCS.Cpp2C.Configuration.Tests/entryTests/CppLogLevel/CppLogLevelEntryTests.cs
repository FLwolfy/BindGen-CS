using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class CppLogLevelEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void CppLogLevel_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void CppLogLevel_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.error.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.error.json", "expected.bindings.error.json");
    }
}
