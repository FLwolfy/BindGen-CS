using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownExtensionNamesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownExtensionNames_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
