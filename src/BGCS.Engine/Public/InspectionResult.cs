using System.Collections.Immutable;
using BGCS.Core;
using BGCS.Core.Bridge;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS;

/// <summary>Exposes detached pipeline models for one resolved target.</summary>
/// <param name="targetId">The resolved native ABI identity.</param>
/// <param name="native">The transformed native facts used by analysis.</param>
/// <param name="bridge">The typed C++ bridge, or null for C input.</param>
/// <param name="interop">The final C ABI and semantic contracts.</param>
/// <param name="managed">The managed projection, or null for bridge-only requests.</param>
public sealed record TargetInspection(
    string targetId,
    NativeModule native,
    NativeBridgePlan? bridge,
    InteropModule interop,
    ManagedApiModel? managed
)
{
    /// <summary>Gets the resolved target and pinned toolchain already used for native fact extraction, or null for caller-created inspection data.</summary>
    public NativeTargetDescriptor? resolvedTarget { get; init; }

}

/// <summary>Reports a complete generation inspection without replacing formal output.</summary>
/// <param name="success">Whether all required inspection stages completed successfully.</param>
/// <param name="targets">Detached models in configured target order.</param>
/// <param name="diagnostics">Accurate pipeline diagnostics after all session resources release.</param>
public sealed record InspectionResult(
    bool success,
    ImmutableArray<TargetInspection> targets,
    ImmutableArray<BindingDiagnostic> diagnostics
);
