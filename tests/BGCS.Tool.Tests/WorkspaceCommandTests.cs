using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class WorkspaceCommandTests
{
    [Fact]
    public void ValidateJsonProducesOneCompleteDocumentForSeveralProjectsWithoutPublishing()
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "native.h"), "int native_value(void);");
            foreach (string id in new[] { "first", "second" })
                File.WriteAllText(Path.Combine(directory, id + ".json"), $$"""
                    { "schemaVersion": 1, "library": { "id": "{{id}}", "nativeName": "native" },
                      "source": { "headers": ["native.h"] }, "output": { "root": "{{id}}-generated" } }
                    """);
            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                { "schemaVersion": 1, "projects": [
                  { "id": "first", "configuration": "first.json" },
                  { "id": "second", "configuration": "second.json", "dependencies": ["first"] }
                ] }
                """);
            using StringWriter output = new();
            using StringWriter error = new();

            int code = CliInvocation.Run(["workspace", "validate", "workspace.json", "--json"], directory, output, error);

            Assert.True(code == 0, error.ToString());
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            Assert.True(report.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2, report.RootElement.GetProperty("projects").GetArrayLength());
            Assert.False(Directory.Exists(Path.Combine(directory, "first-generated")));
            Assert.False(Directory.Exists(Path.Combine(directory, "second-generated")));
        });
    }

    [Fact]
    public void Validate_RejectsUnknownWorkspaceOptions()
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                { "schemaVersion": 1, "projects": [], "UnknownOption": true }
                """);
            using StringWriter error = new();
            Assert.Equal(2, CliInvocation.Run(["workspace", "validate", "workspace.json"], directory, TextWriter.Null, error));
            Assert.Contains("UnknownOption", error.ToString());
        });
    }

    [Fact]
    public void Generate_UsesConfiguredSingleFilePathAndRejectsCorruptedOwnedOutputDuringDiff()
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "native.h"), "int native_add(int left, int right);");
            File.WriteAllText(Path.Combine(directory, "bindgen.json"), """
                { "schemaVersion": 1, "library": { "id": "native", "nativeName": "native" },
                  "source": { "language": "c", "headers": ["native.h"] },
                  "api": { "namespace": "Workspace.Generated", "name": "Native" },
                  "output": { "root": "Generated", "layout": "singleFile" } }
                """);
            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                { "schemaVersion": 1, "projects": [{ "id": "native", "configuration": "bindgen.json" }] }
                """);
            using StringWriter error = new();
            Assert.Equal(0, CliInvocation.Run(["workspace", "generate", "workspace.json"], directory, TextWriter.Null, error));
            string bindings = Directory.GetFiles(Path.Combine(directory, "Generated"), "Bindings.cs", SearchOption.AllDirectories).Single();
            Assert.Equal(0, CliInvocation.Run(["workspace", "diff", "workspace.json"], directory, TextWriter.Null, error));
            File.AppendAllText(bindings, "// drift\n");
            Assert.Equal(2, CliInvocation.Run(["workspace", "diff", "workspace.json"], directory, TextWriter.Null, error));
            Assert.EndsWith("// drift\n", File.ReadAllText(bindings));
        });
    }

    [Fact]
    public void ChangedConfiguration_DiffReportsDifferencesWithoutReplacingFormalOutput()
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "native.h"), "int native_value(void);");
            string configuration = """
                { "schemaVersion": 1, "library": { "id": "native", "nativeName": "native" },
                  "source": { "headers": ["native.h"] }, "api": { "namespace": "Before", "name": "Native" } }
                """;
            File.WriteAllText(Path.Combine(directory, "bindgen.json"), configuration);
            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                { "schemaVersion": 1, "projects": [{ "id": "native", "configuration": "bindgen.json" }] }
                """);
            using StringWriter error = new();
            Assert.Equal(0, CliInvocation.Run(["workspace", "generate", "workspace.json"], directory, TextWriter.Null, error));
            string source = Directory.GetFiles(Path.Combine(directory, "Generated"), "*.cs", SearchOption.AllDirectories).First();
            string original = File.ReadAllText(source);
            File.WriteAllText(Path.Combine(directory, "bindgen.json"), configuration.Replace("Before", "After", StringComparison.Ordinal));
            Assert.Equal(1, CliInvocation.Run(["workspace", "diff", "workspace.json"], directory, TextWriter.Null, error));
            Assert.Equal(original, File.ReadAllText(source));
        });
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bgcs-workspace-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
