using Xunit;

namespace BGCS.Configuration.Tests;

public class AutoSquashTypedefEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AutoSquashTypedef_False_ShouldKeepTypedefInBindings()
    {
        using var output = Generate("config.false.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.false.json", "expected.bindings.false.json");
    }

    [Fact]
    public void AutoSquashTypedef_True_ShouldSquashTypedefInBindings()
    {
        using var output = Generate("config.true.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.true.json", "expected.bindings.true.json");
    }
}
