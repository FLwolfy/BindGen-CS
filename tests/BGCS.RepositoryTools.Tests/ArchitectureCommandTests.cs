using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class ArchitectureCommandTests
{
    [Fact]
    public void Validate_AcceptsTheReviewedGeneratorAndExtensionBoundaries()
    {
        using var repository = new ArchitectureFixture();
        Assert.True(repository.projectCount >= 9);
        Assert.Equal(0, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS.NativeBuild")]
    [InlineData("BGCS.MSBuild")]
    public void Validate_RejectsRetiredImplementationProjects(string retired)
    {
        using var repository = new ArchitectureFixture();
        string path = Path.Combine(repository.root, "eng", "architecture", "project-dependencies.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["projects"]![retired] = new JsonArray();
        File.WriteAllText(path, manifest.ToJsonString());
        Assert.Equal(1, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS.Infrastructure", "<ItemGroup><ProjectReference Include='../BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>")]
    [InlineData("BGCS.Core", "<ItemGroup><Compile Include='../BGCS/BindingModule.cs' Link='BindingModule.cs'/></ItemGroup>")]
    [InlineData("BGCS.Runtime", "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>")]
    [InlineData("BGCS.NativeBridge", "<ItemGroup><ProjectReference Include='../BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>")]
    [InlineData("BGCS.CSharp", "<ItemGroup><ProjectReference Include='../BGCS.NativeBuild/BGCS.NativeBuild.csproj'/></ItemGroup>")]
    [InlineData("BGCS.Engine", "<ItemGroup><ProjectReference Include='../BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>")]
    public void Validate_RejectsDependencyOwnershipAndExecutableViolations(
        string project,
        string content
    ) {
        using var repository = new ArchitectureFixture();
        repository.WriteProject(project, content);
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void Validate_RejectsParserObjectsInNeutralSource()
    {
        using var repository = new ArchitectureFixture();
        File.WriteAllText(Path.Combine(repository.root, "src", "BGCS.Core", "Invalid.cs"),
            "using BGCS.Frontend.Clang.Model; public class Invalid { public CppCompilation ast; }");
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void Validate_RejectsAdditionalProductionProject()
    {
        using var repository = new ArchitectureFixture();
        repository.WriteProject("BGCS.Extra", string.Empty);
        Assert.Equal(1, repository.Validate());
    }

    [Theory]
    [InlineData("BGCS")]
    [InlineData("BGCS.NativeBridge")]
    public void Validate_RejectsNativeParserCallsOutsideTheFrontend(string project)
    {
        using var repository = new ArchitectureFixture();
        File.WriteAllText(Path.Combine(repository.root, "src", project, "Invalid.cs"),
            "using ClangSharp.Interop; public class Invalid { public CXCursor cursor; }");
        Assert.Equal(1, repository.Validate());
    }

}
