using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Explicitly selects one source-language interface for native virtual callback proxy generation.</summary>
/// <param name="id">The unique callback interface identity.</param>
/// <param name="nativeInterface">The exact qualified native class name.</param>
public sealed record VirtualCallbackRequest(
    string id,
    string nativeInterface
)
{
    /// <summary>Gets the explicit cleanup thread assertion: unknown preserves raw-only cleanup; any asserts the generated concrete destructor is valid on every closing thread.</summary>
    public string releaseThreadDomain { get; init; } = "unknown";
}

/// <summary>Describes one exact C callback value carrier independently of managed thunk syntax.</summary>
/// <param name="nativeType">The corresponding source-language value type.</param>
/// <param name="cType">The fixed scalar or explicitly opaque borrowed pointer carrier.</param>
public sealed record VirtualCallbackValue(
    StableId nativeType,
    string cType
);

/// <summary>Describes one status-returning Cdecl callback slot with an optional result-out channel.</summary>
/// <param name="nativeMethod">The exact source-language virtual method.</param>
/// <param name="name">The stable slot identity.</param>
/// <param name="parameters">The C callback inputs after the explicit user-data argument.</param>
/// <param name="result">The optional C result-out storage.</param>
public sealed record VirtualCallbackSlot(
    StableId nativeMethod,
    string name,
    ImmutableArray<VirtualCallbackValue> parameters,
    VirtualCallbackValue? result
)
{
    /// <summary>Gets the typed normalized callback signature independently checked in the final generated C header.</summary>
    public StableId? bridgeCallableType { get; init; }
}

/// <summary>Owns typed virtual proxy declarations and exact raw callback ABI metadata.</summary>
/// <param name="id">The explicit interface selection identity.</param>
/// <param name="nativeInterface">The original interface type identity.</param>
/// <param name="proxyType">The generated concrete native class name.</param>
/// <param name="factory">The generated fixed native factory qualified name.</param>
/// <param name="slots">The immutable Cdecl status/result callback slots.</param>
/// <param name="source">The finite generated source with declared dependencies and content identity.</param>
public sealed record CppVirtualProxyPlan(
    string id,
    StableId nativeInterface,
    string proxyType,
    string factory,
    ImmutableArray<VirtualCallbackSlot> slots,
    ControlledSourceFragment source
)
{
    /// <summary>Gets a bridge-owned generated identity, never an invented original native type fact.</summary>
    public StableId generatedType => new("virtual-proxy-type:" + id);
    /// <summary>Gets the explicitly supplied cleanup thread assertion, independently of native retention.</summary>
    public string releaseThreadDomain { get; init; } = "unknown";
    /// <summary>Gets the source of the cleanup thread claim without promoting it to native verification.</summary>
    public Evidence cleanupEvidence { get; init; } = new(EvidenceLevel.Unknown, "virtual-proxy-cleanup-thread");
}
