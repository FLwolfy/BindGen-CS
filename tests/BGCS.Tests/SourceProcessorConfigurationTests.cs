using System;
using System.IO;
using BGCS.Configuration;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceProcessorConfigurationTests
{
    [Theory]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("zero-timeout")]
    [InlineData("unknown-kind")]
    [InlineData("mixed-add")]
    [InlineData("case-collision")]
    [InlineData("literal-regex-option")]
    public void InvalidConfiguredOperationsFailDuringRequestFreeze(string fault)
    {
        SourceProcessorConfiguration literal = new() { id = "edit", match = "original", replacement = "changed" };
        SourceProcessorConfiguration add = new() { id = "add", kind = SourceProcessorKind.AddSource, path = "Extra.cs", content = "class Extra { }" };
        SourceProcessorConfiguration[] rules = fault switch
        {
            "duplicate" => [literal, literal],
            "traversal" => [literal with { paths = ["../Api.cs"] }],
            "zero-timeout" => [literal with { kind = SourceProcessorKind.RegexReplace, timeoutMilliseconds = 0 }],
            "unknown-kind" => [literal with { kind = (SourceProcessorKind)999 }],
            "mixed-add" => [add with { match = "forbidden" }],
            "case-collision" => [add, add with { id = "second", path = "extra.cs" }],
            _ => [literal with { multiline = true }]
        };
        BindingConfiguration configuration = Configuration() with { output = new() { processors = [.. rules] } };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void GroupedSchemaAndFrozenRequestExposeAllFiniteChoicesWithoutExecutableTypes()
    {
        SourceProcessorConfiguration rule = new() { id = "patch", kind = SourceProcessorKind.RegexReplace, paths = ["Types/**/*.cs"],
            match = "(?<name>old)", replacement = "${name}_new", timeoutMilliseconds = 321, explicitCapture = true };
        GenerationRequest request = new(Configuration() with { output = new() { processors = [rule] } }, Path.GetTempPath());
        Assert.Equal(rule, Assert.Single(request.configuration.output.processors));
        var processors = new SchemaCatalog().CreateBindingSchema()["properties"]!["output"]!["properties"]!["processors"]!;
        Assert.Equal(128, processors["maxItems"]!.GetValue<int>());
        var properties = processors["items"]!["properties"]!;
        Assert.Equal(1, properties["timeoutMilliseconds"]!["minimum"]!.GetValue<int>());
        Assert.Equal(30000, properties["timeoutMilliseconds"]!["maximum"]!.GetValue<int>());
        Assert.Contains("regexReplace", properties["kind"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(3, Enum.GetNames<SourceProcessorKind>().Length);
        Assert.Empty(Configuration().output.processors);
    }

    private static BindingConfiguration Configuration() => new() { schemaVersion = 1, library = new() { id = "processor", nativeName = "processor" }, source = new() { headers = ["native.h"] } };
}
