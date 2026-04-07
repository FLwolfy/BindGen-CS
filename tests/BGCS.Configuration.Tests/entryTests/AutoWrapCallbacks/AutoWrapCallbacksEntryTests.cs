using Xunit;

namespace BGCS.Configuration.Tests;

public class AutoWrapCallbacksEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AutoWrapCallbacks_False_ShouldNotGenerateCallbackHolder()
    {
        using var output = Generate("config.false.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.false.json", "expected.bindings.false.json");
    }

    [Fact]
    public void AutoWrapCallbacks_True_ShouldGenerateCallbackHolder()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
