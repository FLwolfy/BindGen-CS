using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownExtensionPrefixesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownExtensionPrefixes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
