using Xunit;

namespace BGCS.Configuration.Tests;

public class ParameterNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ParameterNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
