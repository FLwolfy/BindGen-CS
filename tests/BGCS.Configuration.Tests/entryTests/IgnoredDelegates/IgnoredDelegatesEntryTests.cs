using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredDelegatesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredDelegates_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
