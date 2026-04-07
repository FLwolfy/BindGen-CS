using Xunit;

namespace BGCS.Configuration.Tests;

public class MemberNamingConventionEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void MemberNamingConvention_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
