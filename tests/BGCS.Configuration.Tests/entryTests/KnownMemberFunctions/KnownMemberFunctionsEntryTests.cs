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

    [Fact]
    public void KnownMemberFunctions_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
    }
}
