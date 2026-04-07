using Xunit;

namespace BGCS.Configuration.Tests;

public class GetLibraryExtensionFunctionNameEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GetLibraryExtensionFunctionName_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
