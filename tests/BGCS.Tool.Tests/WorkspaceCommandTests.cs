using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Tool;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class WorkspaceCommandTests
{
    [Fact]
    public void Validate_RejectsUnknownWorkspaceOptions()
    {
        using TestDirectory directory = new();
        directory.Write("workspace.json", """
            { "Configs": ["bindgen.json"], "UnknownOption": true }
            """);

        Assert.Throws<JsonException>(() => WorkspaceCommand.Run(
            ["validate", directory.Resolve("workspace.json")]));
    }

    [Fact]
    public void Generate_UsesConfiguredSingleFilePathAndDiffsThatPath()
    {
        using TestDirectory directory = new();
        directory.Write("native.h", "int native_add(int left, int right);\n");
        directory.Write("bindgen.json",
            """
            {
              "Preset": "host-c,c-library",
              "Namespace": "Workspace.Generated",
              "ApiName": "Native",
              "LibName": "native",
              "EntryFiles": ["native.h"],
              "AllowedHeaders": ["native.h"],
              "OutputPath": "Generated"
            }
            """);
        directory.Write("workspace.json",
            """
            {
              "Configs": ["bindgen.json"]
            }
            """);

        string manifest = directory.Resolve("workspace.json");
        Assert.Equal(0, WorkspaceCommand.Run(["generate", manifest]));

        string targetOutput = directory.Resolve("Generated");
        Assert.True(File.Exists(Path.Combine(targetOutput, "Bindings.cs")));
        Assert.Empty(Directory.GetDirectories(targetOutput));
        Assert.Equal(0, WorkspaceCommand.Run(["diff", manifest]));

        string bindingsPath = Path.Combine(targetOutput, "Bindings.cs");
        string source = File.ReadAllText(bindingsPath);
        string referenceLine = source.Split('\n').Single(line => line.Contains("ABI reference target:", StringComparison.Ordinal));
        File.WriteAllText(bindingsPath, source.Replace(referenceLine,
            "//     ABI reference target: another-target", StringComparison.Ordinal));
        Assert.Equal(0, WorkspaceCommand.Run(["diff", manifest]));

        File.AppendAllText(Path.Combine(targetOutput, "Bindings.cs"), "// drift\n");
        Assert.Equal(1, WorkspaceCommand.Run(["diff", manifest]));
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgcs-workspace-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Resolve(string relativePath) => System.IO.Path.Combine(Path, relativePath);

        public void Write(string relativePath, string contents)
        {
            string path = Resolve(relativePath);
            string? parent = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
