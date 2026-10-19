using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;

namespace BGCS;

/// <summary>Reports a jointly published artifact set and completed validation stages.</summary>
/// <param name="published">Whether the complete candidate was installed.</param>
/// <param name="outputRoot">The captured formal output path.</param>
/// <param name="artifacts">The exact published logical artifact identities.</param>
/// <param name="diagnostics">Session diagnostics after service release.</param>
public sealed record GenerationResult(
    bool published,
    string outputRoot,
    ImmutableArray<ArtifactDescriptor> artifacts,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets portable native source requirements for each successfully published target.</summary>
    public ImmutableArray<NativeSourceManifest> nativeSources { get; init; } = [];
    /// <summary>Gets session-local physical root bindings; consumers remap these identities to their own frozen inputs.</summary>
    [JsonIgnore]
    public ImmutableArray<NativeInputRootBinding> nativeInputRoots { get; init; } = [];
    /// <summary>Gets detached target models for explicit SDK inspection without embedding them in routine result JSON.</summary>
    [JsonIgnore]
    public ImmutableArray<TargetInspection> targetModels { get; init; } = [];
    /// <summary>Gets each explicitly scoped cache decision; a hit certifies reuse of only the named generation stage.</summary>
    public ImmutableArray<GenerationCacheStageResult> cacheStages { get; init; } = [];
    /// <summary>Gets whether generation and publication completed without errors.</summary>
    public bool success => published && !diagnostics.Any(diagnostic => diagnostic.isFailure);
    /// <summary>Formats diagnostics for caller-owned presentation.</summary>
    /// <returns>A newline-separated diagnostic summary.</returns>
    public string GetDiagnosticSummary() => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.message));
}

/// <summary>Reports reuse of one fully keyed generation stage without claiming whole-pipeline certification.</summary>
/// <param name="targetId">The concrete target identity.</param>
/// <param name="stage">The exact reused stage.</param>
/// <param name="status">Hit, miss or disabled, expressed as a lowercase stable value.</param>
/// <param name="reason">An unavailable fingerprint, disabled policy or unusable entry reason.</param>
public sealed record GenerationCacheStageResult(
    string targetId,
    string stage,
    string status,
    string? reason = null
);
