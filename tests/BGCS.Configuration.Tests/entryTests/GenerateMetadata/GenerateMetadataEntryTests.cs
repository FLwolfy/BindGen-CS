using Xunit;

namespace BGCS.Configuration.Tests;

public class GenerateMetadataEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateMetadata_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
