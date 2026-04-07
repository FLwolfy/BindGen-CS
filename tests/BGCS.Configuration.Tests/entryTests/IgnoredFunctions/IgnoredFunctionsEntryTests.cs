using Xunit;

namespace BGCS.Configuration.Tests;

public class IgnoredFunctionsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IgnoredFunctions_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
