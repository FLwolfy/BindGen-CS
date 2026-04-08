using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateFunctionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateFunctions_False_ShouldSkipFunctionWrappers()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void GenerateFunctions_True_ShouldGenerateFunctionWrappers()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
