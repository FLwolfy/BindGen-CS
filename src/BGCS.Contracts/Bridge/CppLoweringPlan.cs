using System.Collections.Immutable;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;

namespace BGCS.Contracts.Bridge;

/// <summary>Declares a complete detached C representation and its source-language conversion rules.</summary>
/// <param name="type">The primary C representation identity.</param>
/// <param name="types">Every additional representation shape needed by this plan.</param>
/// <param name="parameterConversion">The structured input conversion.</param>
/// <param name="resultConversion">The structured output conversion.</param>
public sealed record CppTypeLoweringPlan(
    StableId type,
    ImmutableArray<BridgeType> types,
    BridgeConversionKind parameterConversion,
    BridgeConversionKind resultConversion
)
{
    /// <summary>Gets an explicit source-language input expression using value and nativeType placeholders.</summary>
    public ControlledSourceFragment? parameterExpression { get; init; }
    /// <summary>Gets named C input carriers for one original parameter; an empty array uses the primary carrier.</summary>
    public ImmutableArray<CppAbiInput> parameterBindings { get; init; } = [];
    /// <summary>Gets an explicit C output expression using value and resultType placeholders.</summary>
    public ControlledSourceFragment? resultExpression { get; init; }
    /// <summary>Gets native helper declarations required by these conversions.</summary>
    public ImmutableArray<ControlledSourceFragment> sourceFragments { get; init; } = [];
    /// <summary>Gets explicit managed conversions associated with this represented native value.</summary>
    public ManagedRecipeProjection? managedProjection { get; init; }
    /// <summary>Gets a real native release declaration to resolve for this explicitly owned result.</summary>
    public CppResultReleaseRequest? ownedResultRelease { get; init; }
}

/// <summary>Declares a custom typed native callable and its complete representation dependencies.</summary>
/// <param name="export">The primary native action and C ABI.</param>
/// <param name="types">Additional C representation shapes.</param>
/// <param name="additionalExports">Explicit helper and release operations.</param>
public sealed record CppCallableLoweringPlan(
    BridgeExport export,
    ImmutableArray<BridgeType> types,
    ImmutableArray<BridgeExport> additionalExports
)
{
    /// <summary>Gets native helper declarations required by this callable protocol.</summary>
    public ImmutableArray<ControlledSourceFragment> sourceFragments { get; init; } = [];
}
