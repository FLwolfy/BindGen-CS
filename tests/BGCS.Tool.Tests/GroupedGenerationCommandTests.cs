using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class GroupedGenerationCommandTests
{
    [Fact]
    public void GenerateJson_ReportsCompleteArtifactsAndDoesNotMixDiagnosticsIntoStdout()
    {
        WithDirectory(directory =>
        {
            WriteInput(directory);
            using StringWriter output = new();
            using StringWriter error = new();
            int exit = CliInvocation.Run(["generate", "bindgen.json", "--json"], directory, output, error);
            Assert.True(exit == 0, error.ToString());
            using JsonDocument json = JsonDocument.Parse(output.ToString());
            Assert.True(json.RootElement.GetProperty("success").GetBoolean());
            Assert.True(json.RootElement.GetProperty("artifacts").GetArrayLength() > 0);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(directory, "Generated"), "*.cs", SearchOption.AllDirectories));
        });
    }

    [Fact]
    public void InspectionAndValidation_DoNotPublishGeneratedOutput()
    {
        WithDirectory(directory =>
        {
            WriteInput(directory);
            using StringWriter output = new();
            using StringWriter error = new();
            Assert.Equal(0, CliInvocation.Run(["inspect", "bindgen.json", "--json"], directory, output, error));
            using JsonDocument inspection = JsonDocument.Parse(output.ToString());
            Assert.True(inspection.RootElement.GetProperty("success").GetBoolean());
            Assert.Single(inspection.RootElement.GetProperty("targets").EnumerateArray());
            Assert.False(Directory.Exists(Path.Combine(directory, "Generated")));
            Assert.Equal(0, CliInvocation.Run(["validate", "bindgen.json"], directory, TextWriter.Null, error));
            Assert.False(Directory.Exists(Path.Combine(directory, "Generated")));
        });
    }

    [Fact]
    public void OutputOverride_UsesTheInvocationDirectoryAndLeavesConfigurationRootUntouched()
    {
        WithDirectory(directory =>
        {
            string configuration = Path.Combine(directory, "config");
            Directory.CreateDirectory(configuration);
            WriteInput(configuration);
            using StringWriter error = new();
            Assert.Equal(0, CliInvocation.Run(["generate", "config/bindgen.json", "--output", "Consumer Generated"], directory, TextWriter.Null, error));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(directory, "Consumer Generated"), "*.cs", SearchOption.AllDirectories));
            Assert.False(Directory.Exists(Path.Combine(configuration, "Generated")));
        });
    }

    [Fact]
    public void LegacyConfiguration_IsRejectedWithoutPublishing()
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "bindgen.json"), "{\"entryFiles\":[\"native.h\"],\"namespace\":\"Old\"}");
            using StringWriter error = new();
            Assert.Equal(2, CliInvocation.Run(["generate", "bindgen.json"], directory, TextWriter.Null, error));
            Assert.False(Directory.Exists(Path.Combine(directory, "Generated")));
        });
    }

    private static void WriteInput(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "native.h"), "int native_add(int left, int right);");
        File.WriteAllText(Path.Combine(directory, "bindgen.json"), """
            { "schemaVersion": 2, "library": { "id": "native", "nativeName": "native" },
              "source": { "headers": ["native.h"] }, "api": { "namespace": "Generated.Native", "name": "NativeApi" } }
            """);
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-grouped-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
