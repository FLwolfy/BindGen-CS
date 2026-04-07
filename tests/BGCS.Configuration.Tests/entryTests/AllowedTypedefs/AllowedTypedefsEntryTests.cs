using Xunit;

namespace BGCS.Configuration.Tests;

public class AllowedTypedefsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void AllowedTypedefs_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
