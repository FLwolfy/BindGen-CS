using BGCS.Core.Requests;
using BGCS.Core.Plugins;
using System.Collections.Immutable;

namespace BGCS.Infrastructure.Workers;

/// <summary>Associates one immutable parse request with a transport identity.</summary>
/// <param name="requestId">The unique request identity.</param>
/// <param name="request">Resolved source inputs.</param>
public sealed record WorkerParseRequest(
    string requestId,
    FrontendRequest request
)
{
    /// <summary>Gets whether this fresh worker captures extension inputs or parses native facts.</summary>
    public WorkerOperationKind operation { get; init; } = WorkerOperationKind.Parse;
    /// <summary>Gets worker-only activation snapshots; the host never loads their implementations.</summary>
    public ImmutableArray<WorkerPluginActivation> pluginActivations { get; init; } = [];
    /// <summary>Gets complete immutable configuration and target information for extension fingerprinting.</summary>
    public CacheFingerprintContext? pluginContext { get; init; }
    /// <summary>Gets preflight state that a new parse worker must independently reproduce.</summary>
    public WorkerPluginCapture? expectedPluginCapture { get; init; }
}

/// <summary>Identifies the single operation performed by a fresh parser process.</summary>
public enum WorkerOperationKind
{
    /// <summary>Parse and return detached native facts.</summary>
    Parse,
    /// <summary>Activate only the declared worker services and capture their complete input identity before cache lookup.</summary>
    CapturePluginInputs
}
