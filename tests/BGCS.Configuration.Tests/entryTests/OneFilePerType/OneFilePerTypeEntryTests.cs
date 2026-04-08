using Xunit;
using System.IO;

namespace BGCS.Configuration.Tests;

public class OneFilePerTypeEntryTests : ConfigurationEntryTestBase
{
    [Fact]
    public void OneFilePerType_ParseHeaderResult_ShouldMatchExpected()
    {
        using var output = Generate("config.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output);
        Assert.False(File.Exists(Path.Combine(output.OutputDirectory, "Bindings.cs")));
        Assert.NotEmpty(Directory.GetFiles(output.OutputDirectory, "*.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void OneFilePerType_AlternateConfig_ShouldMatchExpected()
    {
        using var output = Generate("config.alt.json", ["header.h"], ["header.h"]);
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpected(output, "expected.alt.json", "expected.bindings.alt.json");
        Assert.False(File.Exists(Path.Combine(output.OutputDirectory, "Bindings.cs")));
        Assert.NotEmpty(Directory.GetFiles(output.OutputDirectory, "*.cs", SearchOption.AllDirectories));
    }

    [Fact]
    public void OneFilePerType_True_ShouldGenerateNoFewerFilesThanFalse()
    {
        using var splitByCategory = Generate("config.json", ["header.h"], ["header.h"]);
        using var splitPerType = Generate("config.alt.json", ["header.h"], ["header.h"]);

        AssertGenerationSucceeded(splitByCategory);
        AssertGenerationSucceeded(splitPerType);

        int categoryCount = Directory.GetFiles(splitByCategory.OutputDirectory, "*.cs", SearchOption.AllDirectories).Length;
        int perTypeCount = Directory.GetFiles(splitPerType.OutputDirectory, "*.cs", SearchOption.AllDirectories).Length;
        Assert.True(perTypeCount >= categoryCount);
    }
}
