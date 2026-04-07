using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedFunctionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedFunctions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
