using System.Collections.Generic;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Lowering;

/// <summary>
/// Complete lowering decision for a C++ type. Conversion expressions use the placeholders
/// <c>{value}</c>, <c>{name}</c>, <c>{count}</c>, and <c>{cppType}</c>.
/// </summary>
/// <param name="loweringName">
/// Name of the extension that produced this decision.
/// </param>
/// <param name="kind">
/// Semantic category of the converted value.
/// </param>
/// <param name="cAbiType">
/// Primary C-compatible carrier type.
/// </param>
/// <param name="marshalling">
/// Conversion strategy exposed to binding analysis.
/// </param>
/// <param name="ownership">
/// Ownership of the resulting native carrier.
/// </param>
public sealed record CppTypeLoweringPlan(
    string loweringName,
    CppTypeLoweringKind kind,
    string cAbiType,
    MarshallingStrategy marshalling,
    BindingOwnership ownership
)
{
    /// <summary>
    /// The grouping of C ABI values used to carry the native value.
    /// </summary>
    public CppAbiShape abiShape { get; init; } = CppAbiShape.Direct;
    /// <summary>
    /// Additional C ABI parameters in declaration order; an empty sequence uses the primary ABI value.
    /// </summary>
    public IReadOnlyList<CppAbiParameter> abiParameters { get; init; } = [];
    /// <summary>
    /// Optional expression converting the C argument to a C++ value, using the documented placeholders.
    /// </summary>
    public string? parameterToCppExpression { get; init; }
    /// <summary>
    /// Optional expression converting a returned C++ value to its C representation.
    /// </summary>
    public string? returnToCExpression { get; init; }
    /// <summary>
    /// Whether the produced value owns storage that requires the specified cleanup function.
    /// </summary>
    public bool requiresCleanup { get; init; }
    /// <summary>
    /// Native release symbol for owned storage, or null when no release is required.
    /// </summary>
    public string? cleanupFunction { get; init; }
    /// <summary>
    /// Allocation family required by the cleanup contract.
    /// </summary>
    public BindingAllocatorKind allocatorKind { get; init; } = BindingAllocatorKind.Unspecified;
    /// <summary>
    /// Explicit native allocation symbol, or null when allocation uses the declared family.
    /// </summary>
    public string? allocatorFunction { get; init; }
    /// <summary>
    /// Optional managed conversion expressions accompanying the native lowering.
    /// </summary>
    public CppManagedProjection? managedProjection { get; init; }
    /// <summary>
    /// Native headers required to compile this conversion or invocation.
    /// </summary>
    public IReadOnlyList<string> requiredHeaders { get; init; } = [];
    /// <summary>
    /// Evidence level checked against the selected lowering safety policy.
    /// </summary>
    public CppLoweringSafety safety { get; init; } = CppLoweringSafety.Verified;
}
