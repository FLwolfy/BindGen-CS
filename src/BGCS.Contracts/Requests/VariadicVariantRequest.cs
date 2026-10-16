using System.Collections.Immutable;

namespace BGCS.Contracts.Requests;

/// <summary>Requests one real native adapter with a finite fixed signature for a source variadic function.</summary>
public sealed record VariadicVariantRequest
{
    /// <summary>Gets the exact source declaration, qualified name or unambiguous native callable signature.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the unreserved native adapter name, which is also its default managed source name.</summary>
    public string name { get; init; } = string.Empty;
    /// <summary>Gets only the additional arguments after the original fixed native parameter prefix.</summary>
    public ImmutableArray<VariadicArgumentRequest> parameters { get; init; } = [];
}

/// <summary>Declares a finite additional native argument before the compiler applies the language's default argument promotions.</summary>
/// <param name="name">The unique native parameter name.</param>
/// <param name="nativeType">The native type spelling, resolved by the selected compiler rather than a managed type name.</param>
public sealed record VariadicArgumentRequest(
    string name,
    string nativeType
);
