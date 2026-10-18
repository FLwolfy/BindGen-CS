using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Infrastructure.IO;

namespace BGCS;

/// <summary>Freezes a project's generation request and explicit workspace prerequisites.</summary>
/// <param name="id">The unique ordinal project identity.</param>
/// <param name="request">The independently frozen grouped request.</param>
/// <param name="dependencies">Project identities that must complete successfully first.</param>
public sealed record WorkspaceProject(
    string id,
    GenerationRequest request,
    ImmutableArray<string> dependencies
);

/// <summary>Owns a validated dependency graph without changing the process working directory.</summary>
public sealed class BindingWorkspace
{
    /// <summary>Validates and freezes the complete project graph before any output is created.</summary>
    /// <param name="projects">Independent immutable projects in presentation order.</param>
    /// <param name="parallelism">The maximum number of simultaneously executing projects.</param>
    public BindingWorkspace(
        IEnumerable<WorkspaceProject> projects,
        int parallelism = 2
    ) {
        ArgumentNullException.ThrowIfNull(projects);
        if (parallelism is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(parallelism), "Workspace parallelism must be between 1 and 1024.");
        this.projects = projects.ToImmutableArray();
        if (this.projects.IsEmpty) throw new ArgumentException("A workspace requires at least one project.", nameof(projects));
        this.parallelism = parallelism;
        Dictionary<string, WorkspaceProject> indexed = new(StringComparer.Ordinal);
        foreach (WorkspaceProject project in this.projects)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentException.ThrowIfNullOrWhiteSpace(project.id);
            ArgumentNullException.ThrowIfNull(project.request);
            if (project.dependencies.IsDefault) throw new ArgumentException("Workspace dependencies cannot be an uninitialized collection.", nameof(projects));
            if (!indexed.TryAdd(project.id, project)) throw new ArgumentException($"Workspace project '{project.id}' is duplicated.", nameof(projects));
            if (project.dependencies.Distinct(StringComparer.Ordinal).Count() != project.dependencies.Length) throw new ArgumentException($"Workspace project '{project.id}' repeats a prerequisite.", nameof(projects));
        }
        foreach (WorkspaceProject project in this.projects)
            foreach (string prerequisite in project.dependencies)
                if (string.IsNullOrWhiteSpace(prerequisite) || !indexed.ContainsKey(prerequisite))
                    throw new ArgumentException($"Workspace project '{project.id}' has an unknown prerequisite '{prerequisite}'.", nameof(projects));
        Dictionary<string, int> remaining = indexed.ToDictionary(pair => pair.Key, pair => pair.Value.dependencies.Length, StringComparer.Ordinal);
        Dictionary<string, List<string>> followers = indexed.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (WorkspaceProject project in this.projects)
            foreach (string prerequisite in project.dependencies) followers[prerequisite].Add(project.id);
        SortedSet<string> ready = new(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        List<WorkspaceProject> ordered = [];
        while (ready.Count != 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            ordered.Add(indexed[id]);
            foreach (string follower in followers[id])
                if (--remaining[follower] == 0) ready.Add(follower);
        }
        if (ordered.Count != this.projects.Length)
            throw new ArgumentException($"Workspace dependency cycle includes '{remaining.First(pair => pair.Value != 0).Key}'.", nameof(projects));
        executionOrder = ordered.ToImmutableArray();
        ValidatePaths();
    }

    /// <summary>Gets immutable projects in their declared presentation order.</summary>
    public ImmutableArray<WorkspaceProject> projects { get; }
    /// <summary>Gets the frozen maximum execution concurrency.</summary>
    public int parallelism { get; }
    internal ImmutableArray<WorkspaceProject> executionOrder { get; }

    private void ValidatePaths()
    {
        List<(string Id, string Output)> outputs = [];
        List<(string Id, string Input)> inputs = [];
        foreach (WorkspaceProject project in projects)
        {
            GenerationRequest request = project.request;
            outputs.Add((project.id, Resolve(request.configuration.output.root, request)));
            IEnumerable<string> paths = request.configuration.source.headers.Concat(request.configuration.source.includeDirectories)
                .Concat(request.configuration.source.systemIncludeDirectories)

                .Concat(request.configuration.api.projectionInputs.sources).Concat(request.configuration.api.projectionInputs.references)
                .Concat(request.configurationInputs.Select(input => input.path)).Concat(request.pluginInputs.Select(input => input.path))
                .Append(request.configuration.cache.directory);
            foreach (string path in paths) inputs.Add((project.id, Resolve(path, request)));
        }
        for (int index = 0; index < outputs.Count; index++)
        {
            for (int other = index + 1; other < outputs.Count; other++)
                if (Overlap(outputs[index].Output, outputs[other].Output))
                    throw new ArgumentException($"Workspace output roots overlap for '{outputs[index].Id}' and '{outputs[other].Id}'.");
            foreach ((string id, string input) in inputs)
                if (Overlap(outputs[index].Output, input))
                    throw new ArgumentException($"Workspace output for '{outputs[index].Id}' overlaps a protected input or cache of '{id}'.");
        }
    }

    private static string Resolve(
        string path,
        GenerationRequest request
    ) => Path.TrimEndingDirectorySeparator(PhysicalPathResolver.Resolve(Path.GetFullPath(path, request.baseDirectory)));
    private static bool Overlap(
        string left,
        string right
    ) {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return left.Equals(right, comparison) || left.StartsWith(right + Path.DirectorySeparatorChar, comparison) || right.StartsWith(left + Path.DirectorySeparatorChar, comparison);
    }
}
