using System.IO;
using Xunit;

namespace BGCS.Configuration.Tests;

public class BaseConfigEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void BaseConfig_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
    }

    [Fact]
    public void BaseConfig_SourceMappedCallerPath_UsesCopiedFixtures()
    {
        string mappedPath = Path.Combine(
            Path.GetTempPath(), "source-mapped", "entryTests", "BaseConfig", "BaseConfigEntryTests.cs");

        using var output = Generate("config.json", ["header.h"], ["header.h"], mappedPath);
        AssertGenerationSucceeded(output);
        AssertExpected(output, callerFilePath: mappedPath);
    }
}
