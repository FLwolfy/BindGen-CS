using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredTypesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredTypes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
