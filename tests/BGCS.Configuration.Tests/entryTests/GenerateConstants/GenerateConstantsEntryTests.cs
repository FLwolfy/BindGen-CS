using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateConstantsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateConstants_False_ShouldSkipConstants()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void GenerateConstants_True_ShouldGenerateConstants()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
