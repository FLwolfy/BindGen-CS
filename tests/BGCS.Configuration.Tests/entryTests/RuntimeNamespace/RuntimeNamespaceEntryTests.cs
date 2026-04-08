using Xunit;
using System.IO;

namespace BGCS.Configuration.Tests;

public class RuntimeNamespaceEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void RuntimeNamespace_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
        string runtimePath = Path.Combine(output.OutputDirectory, "Runtime.cs");
        Assert.True(File.Exists(runtimePath));
        Assert.Contains("namespace EntryTests.Runtime", File.ReadAllText(runtimePath));
    }

    [Fact]
    public void RuntimeNamespace_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
        string runtimePath = Path.Combine(output.OutputDirectory, "Runtime.cs");
        Assert.True(File.Exists(runtimePath));
        Assert.Contains("namespace EntryTests.Runtime.Alt", File.ReadAllText(runtimePath));
    }
}
