using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Managed;

/// <summary>Customizes one selected native constant without changing its original value or representation facts.</summary>
/// <param name="constant">The exact native source identity or unambiguous qualified name.</param>
public sealed record ManagedConstantRuleRequest(string constant)
{
    /// <summary>Gets an optional managed identifier override.</summary>
    public string? name { get; init; }
    /// <summary>Gets inert replacement documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets an optional managed constant carrier, checked by actual compile-time validation.</summary>
    public string? managedType { get; init; }
    /// <summary>Gets a typed asserted value, mutually exclusive with expression.</summary>
    public ManagedDefaultValue? value { get; init; }
    /// <summary>Gets an optional bounded compile-time expression; statements, directives and nonconstant values are rejected.</summary>
    public string? expression { get; init; }
}

/// <summary>Customizes an existing native enumeration's public presentation while preserving its ABI carrier.</summary>
/// <param name="nativeType">The exact native enum type identity or unambiguous qualified name.</param>
public sealed record ManagedNativeEnumRuleRequest(string nativeType)
{
    /// <summary>Gets an optional managed type name override.</summary>
    public string? name { get; init; }
    /// <summary>Gets inert replacement type documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets exact existing native member presentation rules; no native member is added or removed.</summary>
    public ImmutableArray<ManagedNativeEnumValueRuleRequest> values { get; init; } = [];
}

/// <summary>Overrides an existing native enum member's managed name, prose or asserted compile-time value.</summary>
/// <param name="member">The exact original member identity or source name.</param>
public sealed record ManagedNativeEnumValueRuleRequest(string member)
{
    /// <summary>Gets an optional managed member name.</summary>
    public string? name { get; init; }
    /// <summary>Gets inert replacement member documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets a typed asserted value fitting the unchanged integral carrier.</summary>
    public ManagedDefaultValue? value { get; init; }
    /// <summary>Gets a bounded compile-time expression, mutually exclusive with value.</summary>
    public string? expression { get; init; }
}

/// <summary>Preserves user-asserted constant presentation independently of compiler-established native facts.</summary>
/// <param name="evidence">The explicit UserAsserted provenance; it cannot be promoted to Verified.</param>
public sealed record ManagedConstantPresentation(Evidence evidence)
{
    /// <summary>Gets optional inert documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets the optional managed constant carrier.</summary>
    public string? managedType { get; init; }
    /// <summary>Gets the optional typed asserted value.</summary>
    public ManagedDefaultValue? value { get; init; }
    /// <summary>Gets the optional compile-time expression assertion.</summary>
    public string? expression { get; init; }
}

/// <summary>Preserves an existing native enum member's asserted managed value and inert prose.</summary>
/// <param name="nativeMember">The unchanged native member identity.</param>
/// <param name="presentation">The asserted presentation.</param>
public sealed record ManagedNativeEnumValuePresentation(
    StableId nativeMember,
    ManagedConstantPresentation presentation
);

/// <summary>Preserves type/member prose and asserted values without replacing native enum ABI facts.</summary>
/// <param name="values">Existing enum member presentation keyed by stable native identity.</param>
public sealed record ManagedNativeEnumPresentation(ImmutableArray<ManagedNativeEnumValuePresentation> values)
{
    /// <summary>Gets optional inert type documentation.</summary>
    public string? documentation { get; init; }
}
