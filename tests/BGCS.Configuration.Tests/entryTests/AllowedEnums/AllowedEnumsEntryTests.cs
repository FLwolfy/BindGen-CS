using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedEnumsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedEnums_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
