using System;
using System.IO;
using System.Linq;
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

        using StringWriter error = new();
        int exitCode = CliInvocation.Run(["workspace", "validate", directory.Resolve("workspace.json")],
            directory.Path, TextWriter.Null, error);
        Assert.Equal(2, exitCode);
        Assert.Contains("UnknownOption", error.ToString());
    }

    [Fact]
    public void Generate_UsesConfiguredSingleFilePathAndDiffsThatPath()
    {
        using TestDirectory directory = new();
        directory.Write("native.h", "int native_add(int left, int right);\n");
        directory.Write("bindgen.json",
            """
            {
              "preset": "host-c,c-library",
              "namespace": "Workspace.Generated",
              "apiName": "Native",
              "libName": "native",
              "entryFiles": [
                "native.h"
              ],
              "allowedHeaders": [
                "native.h"
              ],
              "outputPath": "Generated"
            }
            """);
        directory.Write("workspace.json",
            """
            {
              "Configs": ["bindgen.json"]
            }
            """);

        string manifest = directory.Resolve("workspace.json");
        Assert.Equal(0, Run(["generate", manifest]));

        string targetOutput = directory.Resolve("Generated");
        Assert.True(File.Exists(Path.Combine(targetOutput, "Bindings.cs")));
        Assert.Empty(Directory.GetDirectories(targetOutput));
        Assert.Equal(0, Run(["diff", manifest]));

        string bindingsPath = Path.Combine(targetOutput, "Bindings.cs");
        string source = File.ReadAllText(bindingsPath);
        string referenceLine = source.Split('\n').Single(line => line.Contains("ABI reference target:", StringComparison.Ordinal));
        File.WriteAllText(bindingsPath, source.Replace(referenceLine,
            "//     ABI reference target: another-target", StringComparison.Ordinal));
        Assert.Equal(0, Run(["diff", manifest]));

        File.AppendAllText(Path.Combine(targetOutput, "Bindings.cs"), "// drift\n");
        Assert.Equal(1, Run(["diff", manifest]));
    }

    private static int Run(string[] args) => CliInvocation.Run(
        ["workspace", .. args], Environment.CurrentDirectory, TextWriter.Null, TextWriter.Null);

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
