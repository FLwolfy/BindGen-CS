using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class IncludeFoldersEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void IncludeFolders_WithConfiguredFolders_ShouldGenerateAndMatchExpected()
    {
        using var output = Generate(
            "config.json",
            ["header.h"],
            ["header.h", "includes/dep.hpp"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

}
