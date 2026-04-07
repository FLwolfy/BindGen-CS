using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownMemberFunctionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownMemberFunctions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
