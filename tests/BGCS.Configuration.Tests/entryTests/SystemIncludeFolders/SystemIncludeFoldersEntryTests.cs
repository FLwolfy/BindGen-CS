using Xunit;

namespace BGCS.Configuration.Tests;

public class SystemIncludeFoldersEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void SystemIncludeFolders_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void SystemIncludeFolders_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
        AssertGenerationFailed(output);
    }
}
