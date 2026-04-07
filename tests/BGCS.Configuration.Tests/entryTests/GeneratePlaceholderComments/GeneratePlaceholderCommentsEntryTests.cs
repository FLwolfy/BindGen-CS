using Xunit;

namespace BGCS.Configuration.Tests;

public class GeneratePlaceholderCommentsEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GeneratePlaceholderComments_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
