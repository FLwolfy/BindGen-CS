using Xunit;

namespace BGCS.Configuration.Tests;

public class KeywordsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void Keywords_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        Assert.Contains("customKeyword", output.Config.Keywords);
        AssertBindingsExpected(output);
    }

    [Fact]
    public void Keywords_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        Assert.DoesNotContain("customKeyword", output.Config.Keywords);
        AssertBindingsExpected(output, "expected.bindings.alt.json");
    }
}
