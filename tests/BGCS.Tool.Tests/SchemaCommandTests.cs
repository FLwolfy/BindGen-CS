using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class SchemaCommandTests
{
    [Fact]
    public void Run_DefaultSchema_RejectsUnknownPropertiesAndDescribesCoreFields()
    {
        using TestDirectory directory = new();

        CommandResult result = Run(directory);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument schema = directory.ReadJson("bindgen.schema.json");
        JsonElement root = schema.RootElement;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("array", root.GetProperty("properties").GetProperty("entryFiles").GetProperty("type").GetString());
        Assert.Contains("relative", root.GetProperty("properties").GetProperty("entryFiles").GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DllImport", root.GetProperty("properties").GetProperty("importType").GetProperty("enum").ToString(), StringComparison.Ordinal);
        JsonElement externalTypes = root.GetProperty("properties").GetProperty("externalTypeContracts");
        Assert.Equal("array", externalTypes.GetProperty("type").GetString());
        JsonElement policy = externalTypes.GetProperty("items").GetProperty("properties").GetProperty("byValuePolicy");
        Assert.Contains("RequireLayoutMatch", policy.GetProperty("enum").ToString(), StringComparison.Ordinal);
        Assert.Contains("BypassLayoutValidation", policy.GetProperty("enum").ToString(), StringComparison.Ordinal);
        Assert.False(root.GetProperty("properties").TryGetProperty("headerInjector", out _));
    }

    [Fact]
    public void Run_CppKind_EmitsCppSpecificContract()
    {
        using TestDirectory directory = new();

        CommandResult result = Run(directory, "--kind", "cpp");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument schema = directory.ReadJson("bridge.schema.json");
        JsonElement root = schema.RootElement;
        Assert.Contains("C++ bridge", root.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.True(root.GetProperty("properties").TryGetProperty("templateInstantiations", out _));
        Assert.True(root.GetProperty("properties").TryGetProperty("typeLowerings", out JsonElement typeLowerings));
        Assert.Equal("array", typeLowerings.GetProperty("type").GetString());
        Assert.True(root.GetProperty("properties").TryGetProperty("nativeShims", out _));
        JsonElement csharpSafety = root.GetProperty("properties").GetProperty("cSharpStrictSafetySeverity");
        Assert.Contains("SuppressFriendly", csharpSafety.GetProperty("enum").ToString(), StringComparison.Ordinal);
        Assert.Equal("SuppressFriendly", csharpSafety.GetProperty("default").GetString());
        Assert.Contains("AllowUnsafe", root.GetProperty("properties").GetProperty("loweringSafetyPolicy").GetProperty("enum").ToString(), StringComparison.Ordinal);
        Assert.Equal("entryFiles", root.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void Run_AllowUnknownProperties_IsExplicitOptOut()
    {
        using TestDirectory directory = new();

        CommandResult result = Run(directory, "custom.schema.json", "--allow-unknown-properties");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument schema = directory.ReadJson("custom.schema.json");
        Assert.True(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Run_InvalidKind_FailsWithoutWritingSchema()
    {
        using TestDirectory directory = new();

        CommandResult result = Run(directory, "--kind", "rust");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("c, cpp", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(directory.Resolve("bindgen.schema.json")));
    }

    private static CommandResult Run(TestDirectory directory, params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = CliInvocation.Run(["schema", .. args], directory.Path, output, error);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgcs-schema-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Resolve(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public JsonDocument ReadJson(string relativePath) => JsonDocument.Parse(File.ReadAllText(Resolve(relativePath)));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
