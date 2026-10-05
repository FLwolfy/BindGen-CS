using System;
using System.IO;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class ArchitectureCommandTests
{
    [Fact]
    public void Validate_AcceptsTheEightProjectLibraryBoundaries()
    {
        using var repository = new Repository();
        Assert.Equal(0, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS.Core", "<ItemGroup><ProjectReference Include='../BGCS.CppAst/BGCS.CppAst.csproj'/></ItemGroup>")]
    [InlineData("BGCS.Intermediate", "<ItemGroup><Compile Include='../BGCS/BindingModule.cs' Link='BindingModule.cs'/></ItemGroup>")]
    [InlineData("BGCS.Runtime", "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>")]
    public void Validate_RejectsDependencyOwnershipAndExecutableViolations(
        string project,
        string content
    ) {
        using var repository = new Repository();
        repository.WriteProject(project, content);
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void Validate_RejectsParserObjectsInNeutralSource()
    {
        using var repository = new Repository();
        File.WriteAllText(Path.Combine(repository.root, "src", "BGCS.Intermediate", "Invalid.cs"),
            "using BGCS.CppAst.Model; public class Invalid { public CppCompilation ast; }");
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void Validate_RejectsAdditionalProductionProject()
    {
        using var repository = new Repository();
        repository.WriteProject("BGCS.Extra", string.Empty);
        Assert.Equal(1, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS")]
    [InlineData("BGCS.Cpp2C")]
    public void Validate_RejectsNativeParserCallsOutsideTheFrontend(string project)
    {
        using var repository = new Repository();
        File.WriteAllText(Path.Combine(repository.root, "src", project, "Invalid.cs"),
            "using ClangSharp.Interop; public class Invalid { public CXCursor cursor; }");
        Assert.Equal(1, repository.Validate());
    }

    private sealed class Repository : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-architecture-" + Guid.NewGuid().ToString("N"));

        internal Repository()
        {
            foreach (string project in new[] { "BGCS.Core", "BGCS.CppAst", "BGCS.Intermediate", "BGCS", "BGCS.Cpp2C", "BGCS.Language", "BGCS.Runtime", "BGCS.Tool" })
                WriteProject(project, project == "BGCS.Tool" ? "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>" : string.Empty);
        }

        internal void WriteProject(
            string project,
            string content
        ) {
            string directory = Path.Combine(root, "src", project);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, project + ".csproj"), "<Project Sdk='Microsoft.NET.Sdk'>" + content + "</Project>");
        }

        internal int Validate()
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            return CliInvocation.Run(["validate", "architecture", root], root, output, error);
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
