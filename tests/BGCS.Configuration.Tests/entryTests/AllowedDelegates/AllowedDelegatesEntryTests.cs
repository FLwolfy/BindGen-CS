using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedDelegatesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedDelegates_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
