using Xunit;
using System.IO;

namespace BGCS.Configuration.Tests;

public class MergeGeneratedFilesToSingleFileEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void MergeGeneratedFilesToSingleFile_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
        Assert.False(File.Exists(Path.Combine(output.OutputDirectory, "Bindings.cs")));
        Assert.NotEmpty(Directory.GetFiles(output.OutputDirectory, "*.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void MergeGeneratedFilesToSingleFile_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
        Assert.True(File.Exists(Path.Combine(output.OutputDirectory, "Bindings.cs")));
    }
}
