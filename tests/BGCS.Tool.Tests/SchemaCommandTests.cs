using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class SchemaCommandTests
{
    [Fact]
    public void BindingSchema_UsesStrictGroupedDefaultsAndBothLanguages()
    {
        WithDirectory(directory =>
        {
            Assert.Equal(0, Run(directory));
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "bindgen.schema.json")));
            JsonElement root = schema.RootElement;
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
            JsonElement properties = root.GetProperty("properties");
            Assert.False(properties.TryGetProperty("entryFiles", out _));
            Assert.False(properties.TryGetProperty("nativeBuild", out _));
            Assert.False(properties.TryGetProperty("managedBuild", out _));
            JsonElement api = properties.GetProperty("api").GetProperty("properties");
            Assert.True(api.TryGetProperty("projectionInputs", out _));
            Assert.Equal("functionTable", api.GetProperty("importMode").GetProperty("default").GetString());
            Assert.Equal("instance", api.GetProperty("style").GetProperty("default").GetString());
            Assert.Equal("net10.0", api.GetProperty("framework").GetProperty("const").GetString());
            JsonElement managedEnum = api.GetProperty("enums").GetProperty("items");
            Assert.Equal("[\"name\",\"underlyingType\",\"values\"]", JsonSerializer.Serialize(managedEnum.GetProperty("required")));
            string enumKinds = managedEnum.GetProperty("properties").GetProperty("underlyingType").GetProperty("enum").ToString();
            Assert.DoesNotContain("unknown", enumKinds, StringComparison.Ordinal);
            Assert.Contains("uInt64", enumKinds, StringComparison.Ordinal);
            Assert.Contains("cpp", properties.GetProperty("source").GetProperty("properties").GetProperty("language").GetProperty("enum").ToString());
            Assert.True(properties.TryGetProperty("semantics", out _));
            Assert.True(properties.TryGetProperty("extensions", out _));
        });
    }

    [Fact]
    public void PluginSchema_ExportsTheActualStrictManifestContract()
    {
        WithDirectory(directory =>
        {
            Assert.Equal(0, Run(directory, "--kind", "plugin"));
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "plugin.schema.json")));
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.Equal(1, schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
            Assert.Equal(3, schema.RootElement.GetProperty("properties").GetProperty("plugin").GetProperty("properties").GetProperty("contractsMajor").GetProperty("const").GetInt32());
        });
    }

    [Fact]
    public void WorkspaceSchema_RequiresANonNullVersionedObject()
    {
        WithDirectory(directory =>
        {
            Assert.Equal(0, Run(directory, "--kind", "workspace"));
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "workspace.schema.json")));
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.Equal(1, schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        });
    }

    [Theory]
    [InlineData("--allow-unknown-properties")]
    [InlineData("--kind", "rust")]
    [InlineData("--kind", "cpp")]
    public void UnsupportedSchemaOptions_DoNotWriteOutput(params string[] args)
    {
        WithDirectory(directory =>
        {
            Assert.Equal(2, Run(directory, args));
            Assert.Empty(Directory.GetFiles(directory));
        });
    }

    private static int Run(string directory, params string[] args) => CliInvocation.Run(["schema", .. args], directory, TextWriter.Null, TextWriter.Null);
    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-schema-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
