using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedExtensionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedExtensions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
