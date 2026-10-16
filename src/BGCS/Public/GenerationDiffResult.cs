using System.Collections.Immutable;
using BGCS.Contracts;
using BGCS.Contracts.Common;

namespace BGCS;

/// <summary>Describes an exact logical artifact difference.</summary>
public enum ArtifactChangeKind
{
    /// <summary>The generated candidate adds this artifact.</summary>
    Added,
    /// <summary>The previous output artifact is absent from the candidate.</summary>
    Removed,
    /// <summary>The artifact content digest differs.</summary>
    Changed
}

/// <summary>Records one complete artifact identity difference.</summary>
/// <param name="relativePath">The portable logical artifact path.</param>
/// <param name="kind">The exact difference category.</param>
/// <param name="previous">The verified previous digest, or null for additions.</param>
/// <param name="current">The generated candidate digest, or null for removals.</param>
public sealed record ArtifactChange(
    string relativePath,
    ArtifactChangeKind kind,
    ContentDigest? previous,
    ContentDigest? current
);

/// <summary>Compares a validated candidate with verified output without replacing it; operational cache receipts do not constitute generation changes.</summary>
/// <param name="success">Whether generation and previous artifact validation succeeded.</param>
/// <param name="outputRoot">The captured formal comparison output.</param>
/// <param name="changes">Exact content differences in ordinal path order.</param>
/// <param name="diagnostics">Generation and comparison diagnostics.</param>
public sealed record GenerationDiffResult(
    bool success,
    string outputRoot,
    ImmutableArray<ArtifactChange> changes,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets whether any candidate artifact differs from verified output.</summary>
    public bool hasChanges => !changes.IsDefaultOrEmpty;
}
