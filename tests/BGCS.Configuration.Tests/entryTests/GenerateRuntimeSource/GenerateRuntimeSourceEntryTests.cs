using Xunit;
using System.IO;

namespace BGCS.Configuration.Tests;

public class GenerateRuntimeSourceEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void GenerateRuntimeSource_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
        Assert.True(File.Exists(Path.Combine(output.OutputDirectory, "Runtime.cs")));
    }

    [Fact]
    public void GenerateRuntimeSource_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
        Assert.False(File.Exists(Path.Combine(output.OutputDirectory, "Runtime.cs")));
    }
}
