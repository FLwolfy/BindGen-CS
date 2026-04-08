using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredTypedefsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredTypedefs_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        Assert.Contains("my_typedef", output.Config.IgnoredTypedefs);
        AssertBindingsExpected(output);
    }

    [Fact]
    public void IgnoredTypedefs_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        Assert.DoesNotContain("my_typedef", output.Config.IgnoredTypedefs);
        AssertBindingsExpected(output, "expected.bindings.alt.json");
    }
}
