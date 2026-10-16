using System.Collections.Immutable;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Execution;

namespace BGCS.NativeBuild.Execution;

/// <summary>Retains complete native execution diagnostics and the exact compiler dependency snapshot.</summary>
/// <param name="executions">All dependency scans, identity probes and construction operations.</param>
/// <param name="inputSnapshot">The source, headers and search directories validated before and after native construction.</param>
public sealed record NativeBuildExecutionResult(
    ImmutableArray<ProcessExecutionResult> executions,
    SourceSnapshot inputSnapshot
)
{
    /// <summary>Gets the exact planning-time response and custom argument input snapshot, preserving its independent directory exclusions.</summary>
    public SourceSnapshot argumentInputs { get; init; } = new([], []);
}
