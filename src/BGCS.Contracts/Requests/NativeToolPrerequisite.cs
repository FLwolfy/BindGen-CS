namespace BGCS.Contracts.Requests;

/// <summary>Identifies an exact native tool prerequisite without starting it or creating a build plan.</summary>
/// <param name="role">Compiler, linker, archive, build system, backend or task assembly responsibility.</param>
/// <param name="id">The stable tool identity.</param>
/// <param name="path">The absolute resolved or expected file path; null means no matching tool was found.</param>
/// <param name="requiredOutputKind">The particular artifact form requiring this tool, or null for all forms.</param>
/// <param name="minimumVersion">A declared minimum tool version when the implementation depends on one.</param>
public sealed record NativeToolPrerequisite(
    string role,
    string id,
    string? path,
    NativeOutputKind? requiredOutputKind = null,
    string? minimumVersion = null
);
