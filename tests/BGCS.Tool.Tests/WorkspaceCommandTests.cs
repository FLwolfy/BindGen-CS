using System;
using System.IO;
using BGCS.CppAst.Targeting;
using BGCS.Tool;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class WorkspaceCommandTests
{
    [Fact]
    public void Generate_TargetOutputSubdirectories_IsolatesHostAbiAndDiffsThatTarget()
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
              "TargetOutputSubdirectories": true,
              "Configs": ["bindgen.json"]
            }
            """);

        string manifest = directory.Resolve("workspace.json");
        Assert.Equal(0, WorkspaceCommand.Run(["generate", manifest]));

        string targetOutput = directory.Resolve(Path.Combine("Generated", CppTarget.Resolve().Identifier));
        Assert.True(File.Exists(Path.Combine(targetOutput, "Bindings.cs")));
        Assert.False(File.Exists(directory.Resolve(Path.Combine("Generated", "Bindings.cs"))));
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
