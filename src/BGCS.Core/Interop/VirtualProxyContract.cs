using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Core.Interop;

/// <summary>Preserves one exact callback argument or result-out channel without managed syntax or parser services.</summary>
/// <param name="id">The stable callback channel identity.</param>
/// <param name="index">The zero-based position in the actual native callback prototype.</param>
/// <param name="type">The exact C ABI parameter type; a result-out channel is a pointer.</param>
/// <param name="valueType">The exact represented C value type; a result-out channel names its pointee here.</param>
/// <param name="sourceType">The original C++ value type.</param>
/// <param name="sourceName">The original parameter name, or the fixed result role.</param>
public sealed record VirtualProxyValueContract(
    StableId id,
    int index,
    StableId type,
    StableId valueType,
    StableId sourceType,
    string sourceName
)
{
    /// <summary>Gets the original parameter declaration, or null for a return value.</summary>
    public StableId? sourceDeclaration { get; init; }
    /// <summary>Gets the original source category before normalized C callback conversion.</summary>
    public NativeTypeCategory sourceCategory { get; init; }
    /// <summary>Gets the original primitive role when applicable, including Bool before its uint8 carrier conversion.</summary>
    public string? sourcePrimitiveKind { get; init; }
    /// <summary>Gets the original parameter or method location.</summary>
    public SourceLocation? source { get; init; }
}

/// <summary>Associates an exact source virtual method with a checked Cdecl status callback and its factory slot.</summary>
/// <param name="id">The stable slot identity.</param>
/// <param name="sourceMethod">The original virtual method declaration.</param>
/// <param name="sourceName">The exact unqualified source method name.</param>
/// <param name="callableType">The independently reparsed C function type, rather than an untyped address carrier.</param>
/// <param name="factoryParameter">The actual factory input receiving the callback address.</param>
/// <param name="userDataParameter">The stable first callback argument identity.</param>
/// <param name="parameters">The ordered source argument channels after user data.</param>
/// <param name="result">The optional final result-out channel.</param>
public sealed record VirtualProxySlotContract(
    StableId id,
    StableId sourceMethod,
    string sourceName,
    StableId callableType,
    StableId factoryParameter,
    StableId userDataParameter,
    ImmutableArray<VirtualProxyValueContract> parameters,
    VirtualProxyValueContract? result
)
{
    /// <summary>Gets the exact source overload signature including member qualification.</summary>
    public string sourceSignature { get; init; } = string.Empty;
    /// <summary>Gets the original method location.</summary>
    public SourceLocation? source { get; init; }
}

/// <summary>Describes checked proxy construction, native adjustment and destruction independently of managed thunk implementation.</summary>
/// <param name="id">The stable proxy protocol identity.</param>
/// <param name="name">The explicit callback selection name.</param>
/// <param name="sourceInterface">The original native interface type identity.</param>
/// <param name="proxyType">The concrete opaque C proxy type identity.</param>
/// <param name="interfaceType">The opaque C interface type produced by a real native base cast.</param>
/// <param name="createFunction">The exact status-returning construction export.</param>
/// <param name="destroyFunction">The exact matching cleanup export.</param>
/// <param name="interfaceCastFunction">The real native base-adjustment export.</param>
/// <param name="userDataParameter">The factory's exact user-data argument.</param>
/// <param name="proxyResultParameter">The factory's exact owned result-out parameter.</param>
/// <param name="destroyInstanceParameter">The cleanup export's exact instance argument.</param>
/// <param name="castInstanceParameter">The base-adjustment export's exact proxy input.</param>
/// <param name="interfaceResultParameter">The base-adjustment export's exact borrowed result-out argument.</param>
/// <param name="ownership">The checked factory ownership, allocator and matching module release domain.</param>
/// <param name="slots">The exact callback method protocols.</param>
/// <param name="evidence">The origin of the finite generated proxy protocol; this does not prove caller lifetime.</param>
public sealed record VirtualProxyContract(
    StableId id,
    string name,
    StableId sourceInterface,
    StableId proxyType,
    StableId interfaceType,
    StableId createFunction,
    StableId destroyFunction,
    StableId interfaceCastFunction,
    StableId userDataParameter,
    StableId proxyResultParameter,
    StableId destroyInstanceParameter,
    StableId castInstanceParameter,
    StableId interfaceResultParameter,
    OwnershipContract ownership,
    ImmutableArray<VirtualProxySlotContract> slots,
    Evidence evidence
)
{
    /// <summary>Gets the exact qualified source interface name.</summary>
    public string sourceQualifiedName { get; init; } = string.Empty;
    /// <summary>Gets the source interface location.</summary>
    public SourceLocation? source { get; init; }
    /// <summary>Gets the independently supplied native retention boundary; generated proxy code alone cannot establish it.</summary>
    public LifetimeContract lifetime { get; init; } = new(NativeLifetimeKind.Unknown, null, new(EvidenceLevel.Unknown, "virtual-proxy-lifetime"));
    /// <summary>Gets the native execution acknowledgement required before proxy and callback state destruction.</summary>
    public NativeQuiescenceKind quiescence { get; init; }
}
