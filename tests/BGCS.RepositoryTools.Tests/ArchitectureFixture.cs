using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BGCS.RepositoryTools.Tests;

internal sealed class ArchitectureFixture : IDisposable
{
    private readonly Dictionary<string, string[]> m_dependencies;
    private readonly HashSet<string> m_executables;
    internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-architecture-" + Guid.NewGuid().ToString("N"));
    internal int projectCount => m_dependencies.Count;

    internal ArchitectureFixture()
    {
        DirectoryInfo? actual = new(AppContext.BaseDirectory);
        while (actual is not null && !File.Exists(Path.Combine(actual.FullName, "eng", "architecture", "project-dependencies.json"))) actual = actual.Parent;
        string source = Path.Combine(actual!.FullName, "eng", "architecture", "project-dependencies.json");
        Directory.CreateDirectory(Path.Combine(root, "eng", "architecture"));
        File.Copy(source, Path.Combine(root, "eng", "architecture", "project-dependencies.json"));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(source));
        m_dependencies = manifest.RootElement.GetProperty("projects").EnumerateObject().ToDictionary(
            item => item.Name, item => item.Value.EnumerateArray().Select(value => value.GetString()!).ToArray(), StringComparer.Ordinal);
        m_executables = manifest.RootElement.GetProperty("executables").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach (string project in m_dependencies.Keys) WriteProject(project);
    }

    internal string ProjectDirectory(string project) => Path.Combine(root, "src", project);
    internal void WriteProject(string project, string content = "", bool includeReviewedReferences = true)
    {
        string directory = ProjectDirectory(project);
        Directory.CreateDirectory(directory);
        string references = includeReviewedReferences && m_dependencies.TryGetValue(project, out string[]? dependencies)
            ? "<ItemGroup>" + string.Concat(dependencies.Select(dependency => "<ProjectReference Include='../" + dependency + "/" + dependency + ".csproj'/>")) + "</ItemGroup>" : "";
        string executable = m_executables.Contains(project) ? "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>" : "";
        File.WriteAllText(Path.Combine(directory, project + ".csproj"), "<Project Sdk='Microsoft.NET.Sdk'>" + executable + references + content + "</Project>");
    }
    internal void WriteSource(string project, string content) => File.WriteAllText(Path.Combine(ProjectDirectory(project), "Example.cs"), content);
    internal int Validate()
    {
        using StringWriter output = new(), error = new();
        return CliInvocation.Run(["validate", "architecture", root], root, output, error);
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
}
