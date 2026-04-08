using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateExtensionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateExtensions_True_ShouldGenerateExtensionMethods()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void GenerateExtensions_False_ShouldNotGenerateExtensionMethods()
    {
        using var output = Generate("config.false.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.false.json", "expected.bindings.false.json");
    }
}
