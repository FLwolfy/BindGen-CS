using Xunit;

namespace BGCS.Configuration.Tests;

public class IncludeFoldersEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void IncludeFolders_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
