using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedTypesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedTypes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
