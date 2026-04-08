using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredTypesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredTypes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        Assert.Contains("my_type", output.Config.IgnoredTypes);
        AssertBindingsExpected(output);
    }

    [Fact]
    public void IgnoredTypes_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        Assert.DoesNotContain("my_type", output.Config.IgnoredTypes);
        AssertBindingsExpected(output, "expected.bindings.alt.json");
    }
}
