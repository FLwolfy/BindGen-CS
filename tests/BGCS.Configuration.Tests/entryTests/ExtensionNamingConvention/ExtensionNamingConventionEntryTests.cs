using Xunit;

namespace BGCS.Configuration.Tests;

public class ExtensionNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ExtensionNamingConvention_CamelCase_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void ExtensionNamingConvention_PascalCase_ShouldMatchExpected()
    {
        using var output = Generate("config.pascal.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.pascal.json", "expected.bindings.pascal.json");
    }
}
