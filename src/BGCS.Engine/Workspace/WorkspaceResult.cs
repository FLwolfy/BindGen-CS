using System.Collections.Immutable;
using System.Linq;
using BGCS.Core;

namespace BGCS;

/// <summary>Describes whether a project executed successfully or was prevented by a failed dependency.</summary>
public enum WorkspaceProjectStatus
{
    /// <summary>The requested generation or build completed successfully.</summary>
    Completed,
    /// <summary>The project's own required work failed.</summary>
    Failed,
    /// <summary>A prerequisite failed or was skipped, so this project did not execute.</summary>
    Skipped
}

/// <summary>Reports one project's actual execution and diagnostics.</summary>
/// <param name="projectId">The stable project identity.</param>
/// <param name="status">The observed execution outcome.</param>
/// <param name="generation">The execution result, or null when skipped before execution.</param>
/// <param name="diagnostics">All project diagnostics including prerequisite failures.</param>
public sealed record WorkspaceProjectResult(
    string projectId,
    WorkspaceProjectStatus status,
    GenerationResult? generation,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets whether this project completed the requested work successfully.</summary>
    public bool success => status == WorkspaceProjectStatus.Completed && generation is { success: true };
}

/// <summary>Preserves all project results even when an independent branch fails.</summary>
/// <param name="projects">Execution outcomes in declared presentation order.</param>
public sealed record WorkspaceResult(ImmutableArray<WorkspaceProjectResult> projects)
{
    /// <summary>Gets whether every project completed successfully.</summary>
    public bool success => !projects.IsDefaultOrEmpty && projects.All(project => project.success);
    /// <summary>Gets all project diagnostics in stable presentation order.</summary>
    public ImmutableArray<BindingDiagnostic> diagnostics => projects.SelectMany(project => project.diagnostics).ToImmutableArray();
}
