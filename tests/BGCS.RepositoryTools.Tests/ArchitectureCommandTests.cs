using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class ArchitectureCommandTests
{
    [Fact]
    public void Validate_AcceptsTheElevenGeneratorProjectBoundariesAndWorker()
    {
        using var repository = new Repository();
        Assert.Equal(11, repository.projectCount);
        Assert.Equal(0, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS.NativeBuild")]
    [InlineData("BGCS.MSBuild")]
    public void Validate_RejectsRetiredImplementationProjects(string retired)
    {
        using var repository = new Repository();
        string path = Path.Combine(repository.root, "eng", "architecture", "project-dependencies.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["projects"]![retired] = new JsonArray();
        File.WriteAllText(path, manifest.ToJsonString());
        Assert.Equal(2, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS.Infrastructure", "<ItemGroup><ProjectReference Include='../BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>")]
    [InlineData("BGCS.Contracts", "<ItemGroup><Compile Include='../BGCS/BindingModule.cs' Link='BindingModule.cs'/></ItemGroup>")]
    [InlineData("BGCS.Runtime", "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>")]
    [InlineData("BGCS.Cpp2C", "<ItemGroup><ProjectReference Include='../BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>")]
    [InlineData("BGCS.CSharp", "<ItemGroup><ProjectReference Include='../BGCS.NativeBuild/BGCS.NativeBuild.csproj'/></ItemGroup>")]
    [InlineData("BGCS", "<ItemGroup><ProjectReference Include='../BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>")]
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
        File.WriteAllText(Path.Combine(repository.root, "src", "BGCS.Contracts", "Invalid.cs"),
            "using BGCS.Frontend.Clang.Model; public class Invalid { public CppCompilation ast; }");
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
        internal int projectCount { get; private set; }
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-architecture-" + Guid.NewGuid().ToString("N"));

        internal Repository()
        {
            DirectoryInfo? actual = new(AppContext.BaseDirectory);
            while (actual is not null && !File.Exists(Path.Combine(actual.FullName, "eng", "architecture", "project-dependencies.json")))
                actual = actual.Parent;
            string manifest = Path.Combine(actual!.FullName, "eng", "architecture", "project-dependencies.json");
            Directory.CreateDirectory(Path.Combine(root, "eng", "architecture"));
            File.Copy(manifest, Path.Combine(root, "eng", "architecture", "project-dependencies.json"));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (JsonProperty project in document.RootElement.GetProperty("projects").EnumerateObject())
            {
                projectCount++;
                WriteProject(project.Name, project.Name is "BGCS.Tool" or "BGCS.Frontend.Clang.Worker" ? "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>" : string.Empty);
            }
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
