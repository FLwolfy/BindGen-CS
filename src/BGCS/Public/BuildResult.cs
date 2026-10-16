using System.Collections.Immutable;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;

namespace BGCS;

/// <summary>Reports the output of a candidate build without suggesting unexecuted native calls passed.</summary>
/// <param name="generation">The final joint publication result and diagnostics.</param>
public sealed record BuildResult(GenerationResult generation)
{
    /// <summary>Gets whether required candidate validation and publication succeeded.</summary>
    public bool success => generation.success;
    /// <summary>Gets whether the complete candidate was installed.</summary>
    public bool published => generation.published;
    /// <summary>Gets the final owned output directory.</summary>
    public string outputRoot => generation.outputRoot;
    /// <summary>Gets the complete published artifact collection.</summary>
    public ImmutableArray<ArtifactDescriptor> artifacts => generation.artifacts;
    /// <summary>Gets the generation and build diagnostics.</summary>
    public ImmutableArray<BindingDiagnostic> diagnostics => generation.diagnostics;
    /// <summary>Formats diagnostics for the caller.</summary>
    /// <returns>A newline-separated diagnostic summary.</returns>
    public string GetDiagnosticSummary() => generation.GetDiagnosticSummary();
}
