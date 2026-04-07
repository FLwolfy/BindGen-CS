using Xunit;

namespace BGCS.Configuration.Tests;

public class KnownConstructorsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void KnownConstructors_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
