using System.Collections.Immutable;
using System.Linq;
using BGCS.Core;

namespace BGCS;

/// <summary>Reports one project's independently generated artifact comparison.</summary>
/// <param name="projectId">The stable workspace identity.</param>
/// <param name="status">The actual execution or dependency skip outcome.</param>
/// <param name="diff">The comparison result, or null when not executed.</param>
/// <param name="diagnostics">All comparison or prerequisite diagnostics.</param>
public sealed record WorkspaceDiffProjectResult(
    string projectId,
    WorkspaceProjectStatus status,
    GenerationDiffResult? diff,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets whether the project completed its comparison successfully.</summary>
    public bool success => status == WorkspaceProjectStatus.Completed && diff is { success: true };
}

/// <summary>Preserves dependency-aware workspace comparisons without replacing any formal project output.</summary>
/// <param name="projects">All project outcomes in presentation order.</param>
public sealed record WorkspaceDiffResult(ImmutableArray<WorkspaceDiffProjectResult> projects)
{
    /// <summary>Gets whether every project completed its comparison successfully.</summary>
    public bool success => !projects.IsDefaultOrEmpty && projects.All(project => project.success);
    /// <summary>Gets whether any completed project reports changed artifacts.</summary>
    public bool hasChanges => projects.Any(project => project.diff is { hasChanges: true });
    /// <summary>Gets complete comparison diagnostics in stable project order.</summary>
    public ImmutableArray<BindingDiagnostic> diagnostics => projects.SelectMany(project => project.diagnostics).ToImmutableArray();
}
