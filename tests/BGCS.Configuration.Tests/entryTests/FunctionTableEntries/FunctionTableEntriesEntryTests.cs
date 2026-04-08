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
}
