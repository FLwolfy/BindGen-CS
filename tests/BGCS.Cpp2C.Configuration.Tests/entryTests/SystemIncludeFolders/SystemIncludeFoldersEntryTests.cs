using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class SystemIncludeFoldersEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void SystemIncludeFolders_WithConfiguredFolders_ShouldGenerateAndMatchExpected()
    {
        using var output = Generate(
            "config.json",
            ["header.h"],
            ["header.h", "sysincludes/sysdep.hpp"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

}
