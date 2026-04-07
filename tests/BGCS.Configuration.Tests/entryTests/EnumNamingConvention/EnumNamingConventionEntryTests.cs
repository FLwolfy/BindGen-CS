using Xunit;

namespace BGCS.Configuration.Tests;

public class EnumNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void EnumNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
