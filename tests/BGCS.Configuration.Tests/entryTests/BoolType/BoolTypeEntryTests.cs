using Xunit;

namespace BGCS.Configuration.Tests;

public class BoolTypeEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void BoolType_Bool32_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void BoolType_Bool8_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.bool8.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.bool8.json", "expected.bindings.bool8.json");
    }
}
