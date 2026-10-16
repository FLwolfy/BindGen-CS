using BGCS.Contracts.Common;

namespace BGCS.Contracts.Managed;

/// <summary>Names an existing native operation without duplicating its import, conversions or resource protocol.</summary>
/// <param name="nativeExport">The existing final native function identity.</param>
/// <param name="name">The public managed alias name.</param>
public sealed record ManagedFunctionAlias(
    StableId nativeExport,
    string name
)
{
    /// <summary>Gets optional inert alias-specific prose; null retains the target method's documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets optional parsed alias-specific documentation sections.</summary>
    public SourceDocumentation? documentationContent { get; init; }
}

/// <summary>Requests a managed name for one exact existing operation.</summary>
/// <param name="function">An exact export, source signature or unambiguous source qualified name.</param>
/// <param name="name">The public managed alias name.</param>
public sealed record ManagedFunctionAliasRequest(
    string function,
    string name
)
{
    /// <summary>Gets optional inert alias-specific documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets optional detached documentation sections using original native parameter identities.</summary>
    public SourceDocumentation? documentationContent { get; init; }
}
