using Xunit;

namespace BGCS.Configuration.Tests;

public class ConstantNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void ConstantNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
