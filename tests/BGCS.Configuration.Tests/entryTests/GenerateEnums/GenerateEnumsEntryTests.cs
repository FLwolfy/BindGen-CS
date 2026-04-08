using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateEnums_False_ShouldSkipEnums()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void GenerateEnums_True_ShouldGenerateEnums()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
