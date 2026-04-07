using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownConstantNamesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownConstantNames_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
