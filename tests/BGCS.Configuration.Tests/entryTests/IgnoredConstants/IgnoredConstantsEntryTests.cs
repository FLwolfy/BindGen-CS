using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredConstantsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredConstants_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
