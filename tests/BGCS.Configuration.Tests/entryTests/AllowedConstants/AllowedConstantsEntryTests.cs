using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedConstantsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedConstants_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
