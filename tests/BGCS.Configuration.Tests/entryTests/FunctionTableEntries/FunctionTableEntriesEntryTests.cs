using Xunit;

namespace BGCS.Configuration.Tests;

public class FunctionTableEntriesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void FunctionTableEntries_Index0_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void FunctionTableEntries_Index7_ShouldMatchExpected()
    {
        using var output = Generate("config.index7.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.index7.json", "expected.bindings.index7.json");
    }

    [Fact]
    public void FunctionTableEntries_MissingOrDuplicateIndices_ShouldBeAutoNormalized()
    {
        using var output = Generate("config.auto.json", ["header.auto.h"], ["header.auto.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertBindingsExpected(output, "expected.bindings.auto.json");
    }
}
