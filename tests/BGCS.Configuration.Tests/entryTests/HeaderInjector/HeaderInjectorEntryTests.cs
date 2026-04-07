using Xunit;

namespace BGCS.Configuration.Tests;

public class HeaderInjectorEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void HeaderInjector_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
