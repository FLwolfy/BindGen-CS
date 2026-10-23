using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Managed;

/// <summary>Selects one existing native pointer shape for an explicitly non-owning address carrier.</summary>
/// <param name="nativeType">An exact record, named pointer alias or existing pointer identity.</param>
/// <param name="name">The public managed address-carrier name.</param>
public sealed record ManagedPointerCarrierRequest(
    string nativeType,
    string name
)
{
    /// <summary>Gets the total pointer indirection from the native record; no missing native pointer facts are synthesized.</summary>
    public int pointerDepth { get; init; } = 1;
    /// <summary>Gets whether the first dereferenced native pointee is const; deeper qualifications remain exact native facts.</summary>
    public bool readOnly { get; init; }
    /// <summary>Gets optional inert public documentation; null retains source documentation or the default address description.</summary>
    public string? documentation { get; init; }
}

/// <summary>Names a native address without transferring ownership or guaranteeing pointed-to storage lifetime.</summary>
/// <param name="nativePointer">The exact existing native pointer or pointer-alias identity.</param>
/// <param name="name">The public address-carrier name.</param>
public sealed record ManagedPointerCarrier(
    StableId nativePointer,
    string name
)
{
    /// <summary>Gets optional inert documentation independently of native ABI or lifetime facts.</summary>
    public string? documentation { get; init; }
}

/// <summary>Maps one exact native pointer parameter to a named non-owning address carrier.</summary>
/// <param name="parameter">The native parameter name or identity.</param>
/// <param name="carrier">The selected address-carrier name.</param>
public sealed record ManagedPointerParameterRequest(
    string parameter,
    string carrier
);

/// <summary>Requests an explicit address-based callable surface without changing native imports or lifetime evidence.</summary>
/// <param name="function">The exact native callable selector.</param>
public sealed record ManagedPointerFunctionRequest(string function)
{
    /// <summary>Gets an optional distinct public method name; null retains the checked managed operation name.</summary>
    public string? name { get; init; }
    /// <summary>Gets exact pointer inputs exposed as address carriers.</summary>
    public ImmutableArray<ManagedPointerParameterRequest> parameters { get; init; } = [];
    /// <summary>Gets the optional carrier for an untracked pointer result; tracked ownership and module borrowing cannot be replaced.</summary>
    public string? returns { get; init; }
    /// <summary>Gets an optional selected address parameter exposed as this carrier's receiver; its member requires an explicit API context and adds no lifetime guarantee.</summary>
    public string? receiver { get; init; }
}

/// <summary>Resolves one address parameter to its original native identity.</summary>
/// <param name="nativeParameter">The unchanged native parameter.</param>
/// <param name="carrier">The selected carrier name.</param>
public sealed record ManagedPointerParameter(
    StableId nativeParameter,
    string carrier
);

/// <summary>Preserves explicit address presentation independently of native ABI and resource ownership.</summary>
/// <param name="nativeExport">The unchanged native callable.</param>
/// <param name="parameters">Selected address parameters.</param>
public sealed record ManagedPointerFunction(
    StableId nativeExport,
    ImmutableArray<ManagedPointerParameter> parameters
)
{
    /// <summary>Gets an optional public name independent of the original operation.</summary>
    public string? name { get; init; }
    /// <summary>Gets an untracked pointer result carrier, or null for a supported ordinary result.</summary>
    public string? returns { get; init; }
    /// <summary>Gets the exact selected native address parameter used as a receiver with an explicit API context.</summary>
    public StableId? receiver { get; init; }
}
