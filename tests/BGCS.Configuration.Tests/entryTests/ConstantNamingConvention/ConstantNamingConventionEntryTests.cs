using Xunit;

namespace BGCS.Configuration.Tests;

public class ConstantNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ConstantNamingConvention_PascalCase_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void ConstantNamingConvention_CamelCase_ShouldMatchExpected()
    {
        using var output = Generate("config.camel.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.camel.json", "expected.bindings.camel.json");
    }

    [Fact]
    public void ConstantNamingConvention_ScreamingSnakeCase_ShouldMatchExpected()
    {
        using var output = Generate("config.screaming.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.screaming.json", "expected.bindings.screaming.json");
    }
}
