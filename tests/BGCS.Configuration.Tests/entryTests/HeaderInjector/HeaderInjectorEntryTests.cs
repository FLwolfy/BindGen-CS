using Xunit;

namespace BGCS.Configuration.Tests;

public class HeaderInjectorEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void HeaderInjector_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
