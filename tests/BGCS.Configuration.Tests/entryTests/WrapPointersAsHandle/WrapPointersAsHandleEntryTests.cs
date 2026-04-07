using Xunit;

namespace BGCS.Configuration.Tests;

public class WrapPointersAsHandleEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void WrapPointersAsHandle_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
