using Xunit;

namespace BGCS.Configuration.Tests;

public class EnumNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void EnumNamingConvention_CamelCase_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void EnumNamingConvention_PascalCase_ShouldMatchExpected()
    {
        using var output = Generate("config.pascal.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.pascal.json", "expected.bindings.pascal.json");
    }
}
