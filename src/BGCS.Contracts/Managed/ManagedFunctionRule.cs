using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Managed;

/// <summary>Identifies language-independent constant arguments for finite managed convenience overloads.</summary>
public enum ManagedDefaultValueKind
{
    /// <summary>A null reference or native pointer.</summary>
    Null,
    /// <summary>The default value of the exact projected parameter type.</summary>
    Default,
    /// <summary>A true or false literal.</summary>
    Boolean,
    /// <summary>An exact base-ten integral value.</summary>
    Integer,
    /// <summary>A finite invariant floating-point value.</summary>
    FloatingPoint,
    /// <summary>A managed Unicode string literal.</summary>
    String,
    /// <summary>One UTF-16 character literal.</summary>
    Character,
    /// <summary>A qualified consumer constant or enumeration member.</summary>
    ConstantReference
}

/// <summary>Preserves a constant's intent without embedding source statements or frontend handles.</summary>
/// <param name="kind">The typed constant category.</param>
/// <param name="value">The exact literal text or qualified constant identity; null for null and default.</param>
public sealed record ManagedDefaultValue(
    ManagedDefaultValueKind kind,
    string? value = null
);

/// <summary>Associates a convenience argument with one exact native parameter.</summary>
/// <param name="nativeParameter">The original parameter identity.</param>
/// <param name="value">The explicit managed constant.</param>
public sealed record ManagedDefaultArgument(
    StableId nativeParameter,
    ManagedDefaultValue value
);

/// <summary>Selects an explicit convenience argument without relying on generated parameter spelling.</summary>
/// <param name="parameter">The source parameter name or exact identity.</param>
/// <param name="value">The explicit managed constant.</param>
public sealed record ManagedDefaultArgumentRequest(
    string parameter,
    ManagedDefaultValue value
);

/// <summary>Selects an explicit friendly value conversion while preserving the original native parameter carrier.</summary>
/// <param name="parameter">The original source parameter name or exact identity.</param>
/// <param name="projection">The caller-asserted managed input conversion.</param>
public sealed record ManagedParameterProjectionRequest(
    string parameter,
    ManagedRecipeProjection projection
);

/// <summary>Declares one finite alternate public input signature forwarding through the existing checked operation.</summary>
/// <param name="parameters">Explicit conversions into existing friendly value inputs; native signatures remain unchanged.</param>
public sealed record ManagedInputVariationRequest(ImmutableArray<ManagedParameterProjectionRequest> parameters)
{
    /// <summary>Gets an optional alternate method name; null retains the selected friendly name.</summary>
    public string? name { get; init; }
}

/// <summary>Binds alternate public input conversions to original native parameter identities.</summary>
/// <param name="parameters">Checked input identities and explicit value conversion assertions.</param>
public sealed record ManagedInputVariation(ImmutableArray<ManagedValueProjection> parameters)
{
    /// <summary>Gets an optional alternate public method name.</summary>
    public string? name { get; init; }
}

/// <summary>Customizes one selected callable's presentation and finite constant-argument conveniences.</summary>
/// <param name="function">The exact export, source signature or unambiguous qualified callable.</param>
public sealed record ManagedFunctionRuleRequest(string function)
{
    /// <summary>Gets a finite list of additional public input signatures forwarding through the checked friendly operation.</summary>
    public ImmutableArray<ManagedInputVariationRequest> inputVariations { get; init; } = [];
    /// <summary>Gets an explicit managed name, or null to retain naming policy.</summary>
    public string? name { get; init; }
    /// <summary>Gets inert documentation replacing source prose, or null to retain source documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets explicit constant arguments for checked convenience overloads.</summary>
    public ImmutableArray<ManagedDefaultArgumentRequest> defaults { get; init; } = [];
    /// <summary>Gets explicit friendly input conversions; these never alter raw imports or native storage.</summary>
    public ImmutableArray<ManagedParameterProjectionRequest> parameters { get; init; } = [];
    /// <summary>Gets an explicit friendly result conversion, including the original-result channel of a checked status bridge.</summary>
    public ManagedRecipeProjection? returns { get; init; }
}
