namespace BGCS.Core.Targets;

/// <summary>Requests one explicit execution model for an established native target.</summary>
/// <param name="runtimeId">The requested open execution-model identity.</param>
/// <param name="target">The selected target extension's facts.</param>
public sealed record ManagedRuntimeRequest(
    ManagedRuntimeId runtimeId,
    NativeTargetDescriptor target
);
