using Xunit;

namespace BGCS.Configuration.Tests;

public class GetLibraryNameFunctionNameEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GetLibraryNameFunctionName_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }
}
