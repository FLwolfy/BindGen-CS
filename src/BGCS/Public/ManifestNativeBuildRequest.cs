using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.NativeBuild.Validation;

namespace BGCS;

/// <summary>Requests standalone native construction from a locked neutral generation manifest.</summary>
/// <param name="manifestPath">The declaring bridge.manifest.json path.</param>
public sealed record ManifestNativeBuildRequest(string manifestPath)
{
    /// <summary>Gets a provider override, or auto to retain the manifest's provider selection.</summary>
    public string providerId { get; init; } = "auto";
    /// <summary>Gets an authoritative absolute compiler override.</summary>
    public string? compilerPath { get; init; }
    /// <summary>Gets an authoritative absolute build-system tool override.</summary>
    public string? buildToolPath { get; init; }
    /// <summary>Gets an authoritative absolute auxiliary build tool override.</summary>
    public string? auxiliaryToolPath { get; init; }
    /// <summary>Gets an authoritative absolute symbol inspection tool override.</summary>
    public string? exportToolPath { get; init; }
    /// <summary>Gets an explicitly named absolute output artifact, or null for owned artifact-root publication.</summary>
    public string? outputFile { get; init; }
    /// <summary>Gets an optional independently owned package asset root.</summary>
    public string? packageRoot { get; init; }
    /// <summary>Gets whether exact required native symbols are verified.</summary>
    public bool verifyExports { get; init; } = true;
    /// <summary>Gets the positive complete native construction deadline in seconds.</summary>
    public int timeoutSeconds { get; init; } = 900;
}

/// <summary>Reports a complete immutable standalone native plan and preflight diagnostics.</summary>
/// <param name="plan">The plan, or null when preflight failed.</param>
/// <param name="diagnostics">All preflight diagnostics.</param>
public sealed record ManifestNativeBuildPlanResult(
    BGCS.Contracts.Requests.NativeBuildPlan? plan,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets the declared final native artifact path; construction plan outputs remain private candidates.</summary>
    public string? nativeArtifact { get; init; }
    /// <summary>Gets whether complete preflight produced a valid native construction plan.</summary>
    public bool success => plan is not null && !diagnostics.Any(diagnostic => diagnostic.isFailure);
    /// <summary>Formats preflight diagnostics for caller-owned presentation.</summary>
    /// <returns>A newline-separated diagnostic summary.</returns>
    public string GetDiagnosticSummary() => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.message));
}

/// <summary>Reports verified native construction and complete publication without implying runtime invocation.</summary>
/// <param name="published">Whether all requested artifacts were published.</param>
/// <param name="plan">The immutable executed native plan, or null on preflight failure.</param>
/// <param name="nativeArtifact">The exact published native file, or null on failure.</param>
/// <param name="exportEvidence">Actual symbol verification, or null when explicitly skipped.</param>
/// <param name="packageArtifacts">The complete published package artifact identities.</param>
/// <param name="diagnostics">All preflight, execution and publication diagnostics.</param>
public sealed record ManifestNativeBuildResult(
    bool published,
    BGCS.Contracts.Requests.NativeBuildPlan? plan,
    string? nativeArtifact,
    NativeExportInspectionResult? exportEvidence,
    ImmutableArray<ArtifactDescriptor> packageArtifacts,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets actual native literal proof receipts, including for explicitly requested single-file publication.</summary>
    public ImmutableArray<NativeStringProofReceipt> literalProofs { get; init; } = [];
    /// <summary>Gets whether construction, verification and publication completed without errors.</summary>
    public bool success => published && !diagnostics.Any(diagnostic => diagnostic.isFailure);
    /// <summary>Formats session diagnostics for caller-owned presentation.</summary>
    /// <returns>A newline-separated diagnostic summary.</returns>
    public string GetDiagnosticSummary() => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.message));
}
