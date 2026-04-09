using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class NamePrefixEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void NamePrefix_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void NamePrefix_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
    }
}
