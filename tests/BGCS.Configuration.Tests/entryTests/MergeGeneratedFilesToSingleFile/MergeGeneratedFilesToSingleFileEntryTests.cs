using Xunit;

namespace BGCS.Configuration.Tests;

public class MergeGeneratedFilesToSingleFileEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void MergeGeneratedFilesToSingleFile_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
