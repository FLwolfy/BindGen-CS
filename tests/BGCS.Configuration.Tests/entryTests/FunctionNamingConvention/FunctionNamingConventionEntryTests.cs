using Xunit;

namespace BGCS.Configuration.Tests;

public class FunctionNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void FunctionNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
