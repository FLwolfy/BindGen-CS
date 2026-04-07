using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownEnumPrefixesEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownEnumPrefixes_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
