using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateHandlesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateHandles_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
