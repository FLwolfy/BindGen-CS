using Xunit;

namespace BGCS.Configuration.Tests;

public class CppLogLevelEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void CppLogLevel_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
