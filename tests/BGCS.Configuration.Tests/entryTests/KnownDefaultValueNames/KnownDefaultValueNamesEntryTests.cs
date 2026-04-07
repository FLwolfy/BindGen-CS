using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownDefaultValueNamesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownDefaultValueNames_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
