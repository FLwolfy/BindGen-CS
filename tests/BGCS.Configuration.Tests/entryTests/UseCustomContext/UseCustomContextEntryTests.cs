using Xunit;

namespace BGCS.Configuration.Tests;

public class UseCustomContextEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void UseCustomContext_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
