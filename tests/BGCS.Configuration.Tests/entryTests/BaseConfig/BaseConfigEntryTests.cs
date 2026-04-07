using Xunit;

namespace BGCS.Configuration.Tests;

public class BaseConfigEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void BaseConfig_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
