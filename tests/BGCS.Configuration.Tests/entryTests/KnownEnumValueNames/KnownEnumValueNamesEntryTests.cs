using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownEnumValueNamesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownEnumValueNames_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
