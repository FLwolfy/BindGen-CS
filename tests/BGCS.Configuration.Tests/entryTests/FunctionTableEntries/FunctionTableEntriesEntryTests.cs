using Xunit;

namespace BGCS.Configuration.Tests;

public class FunctionTableEntriesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void FunctionTableEntries_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
