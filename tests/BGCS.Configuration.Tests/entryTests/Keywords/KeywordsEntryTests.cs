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
        AssertExpected(output);
    }
}
