using BGCS.Core.Interop;
using BGCS.Core.Targets;

namespace BGCS.Core.Calls;

/// <summary>Supplies detached storage and execution facts to one ABI provider.</summary>
/// <param name="module">The physical type graph.</param>
/// <param name="target">The established target facts.</param>
/// <param name="runtime">The selected execution capabilities.</param>
/// <param name="signature">The complete forward or reverse signature.</param>
public sealed record CallAbiRequest(
    InteropModule module,
    NativeTargetDescriptor target,
    ManagedRuntimeProfile runtime,
    NativeCallSignature signature
);
