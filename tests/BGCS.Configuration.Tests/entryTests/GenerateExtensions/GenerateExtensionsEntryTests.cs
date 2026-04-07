using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateExtensionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateExtensions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
