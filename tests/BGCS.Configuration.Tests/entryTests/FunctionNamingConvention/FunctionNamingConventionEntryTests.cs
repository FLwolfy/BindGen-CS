using Xunit;

namespace BGCS.Configuration.Tests;

public class FunctionNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void FunctionNamingConvention_CamelCase_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void FunctionNamingConvention_PascalCase_ShouldMatchExpected()
    {
        using var output = Generate("config.pascal.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.pascal.json", "expected.bindings.pascal.json");
    }
}
