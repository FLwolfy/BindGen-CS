using System.IO;
using Xunit;

namespace BGCS.Cpp2C.Configuration.Tests;

public class BaseConfigEntryTests : Cpp2CConfigurationEntryTestBase
{
    [Fact]
    public void BaseConfig_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate(
            "config.json",
            ["header.h"],
            ["header.h", "includes/dep.hpp"]);
        PrintGeneratedOutput(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void BaseConfig_SourceMappedCallerPath_UsesCopiedFixtures()
    {
        string mappedPath = Path.Combine(
            Path.GetTempPath(), "source-mapped", "entryTests", "BaseConfig", "BaseConfigEntryTests.cs");

        using var output = Generate(
            "config.json",
            ["header.h"],
            ["header.h", "includes/dep.hpp"],
            mappedPath);
        AssertGenerationSucceeded(output);
        AssertExpected(output, callerFilePath: mappedPath);
    }
}
