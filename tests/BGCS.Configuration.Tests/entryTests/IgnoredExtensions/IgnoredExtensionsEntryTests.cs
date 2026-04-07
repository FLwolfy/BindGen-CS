using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredExtensionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredExtensions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
