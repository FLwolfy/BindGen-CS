using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;

namespace BGCS;

/// <summary>Reports native facts, input dependencies and diagnostics after all frontend resources are released.</summary>
/// <param name="module">The native module, or null when parsing could not complete.</param>
/// <param name="snapshot">The parser input snapshot, or null when parsing could not complete.</param>
/// <param name="diagnostics">All session diagnostics.</param>
public sealed record AnalysisResult(
    NativeModule? module,
    SourceSnapshot? snapshot,
    ImmutableArray<BindingDiagnostic> diagnostics
)
{
    /// <summary>Gets the resolved target facts, or null when resolution or parsing failed.</summary>
    public NativeTargetDescriptor? target { get; init; }
    /// <summary>Gets response/configuration file bytes and negative lookup inputs captured before native parsing.</summary>
    public SourceSnapshot? argumentInputs { get; init; }
    internal BGCS.Contracts.Requests.FrontendRequest? preparedFrontend { get; init; }
    internal SourceSnapshot? frontendExecutionInputs { get; init; }
    /// <summary>Gets whether parsing and neutral graph verification succeeded.</summary>
    public bool success => module is not null && !diagnostics.Any(diagnostic => diagnostic.isFailure);

    /// <summary>Formats the diagnostic messages for a caller-owned presentation layer.</summary>
    /// <returns>A newline-separated diagnostic summary.</returns>
    public string GetDiagnosticSummary() => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.message));
}
