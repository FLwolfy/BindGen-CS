using Xunit;

namespace BGCS.Configuration.Tests;

public class TypeNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void TypeNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
