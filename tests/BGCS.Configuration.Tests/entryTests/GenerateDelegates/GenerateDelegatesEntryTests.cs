using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateDelegatesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateDelegates_False_ShouldNotGenerateDelegateType()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void GenerateDelegates_True_ShouldGenerateDelegateType()
    {
        using var output = Generate("config.true.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
