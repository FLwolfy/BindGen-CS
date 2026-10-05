using System.Collections.Generic;
using BGCS.Intermediate;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Declarative type-lowering recipe serialized in bridge configuration.</summary>
public sealed class CppTypeLoweringRecipe
{
    /// <summary>
    /// Stable registration name, unique within this extension category.
    /// </summary>
    public string name { get; set; } = string.Empty;
    /// <summary>
    /// Native type-name glob; an asterisk matches any sequence and spaces are ignored.
    /// </summary>
    public string typePattern { get; set; } = string.Empty;
    /// <summary>
    /// Selection priority; higher values are tried first and equal priorities use ordinal name order.
    /// </summary>
    public int priority { get; set; } = 100;
    /// <summary>
    /// Semantic category assigned to the lowered value.
    /// </summary>
    public CppTypeLoweringKind kind { get; set; } = CppTypeLoweringKind.Custom;
    /// <summary>
    /// Primary C-compatible carrier type written into the bridge.
    /// </summary>
    public string cAbiType { get; set; } = "void*";
    /// <summary>
    /// Conversion strategy exposed to managed binding analysis.
    /// </summary>
    public MarshallingStrategy marshalling { get; set; } = MarshallingStrategy.Handle;
    /// <summary>
    /// Ownership of the native carrier produced by this recipe.
    /// </summary>
    public BindingOwnership ownership { get; set; } = BindingOwnership.Borrowed;
    /// <summary>
    /// The grouping of C ABI values used to carry the native value.
    /// </summary>
    public CppAbiShape abiShape { get; set; } = CppAbiShape.Direct;
    /// <summary>
    /// Additional C ABI parameters in declaration order; an empty sequence uses the primary ABI value.
    /// </summary>
    public List<CppAbiParameter> abiParameters { get; set; } = [];
    /// <summary>
    /// Optional expression converting the C argument to a C++ value, using the documented placeholders.
    /// </summary>
    public string? parameterToCppExpression { get; set; }
    /// <summary>
    /// Optional expression converting a returned C++ value to its C representation.
    /// </summary>
    public string? returnToCExpression { get; set; }
    /// <summary>
    /// Whether the produced value owns storage that requires the specified cleanup function.
    /// </summary>
    public bool requiresCleanup { get; set; }
    /// <summary>
    /// Native release symbol for owned storage, or null when no release is required.
    /// </summary>
    public string? cleanupFunction { get; set; }
    /// <summary>
    /// Allocation family required by the cleanup contract.
    /// </summary>
    public BindingAllocatorKind allocatorKind { get; set; } = BindingAllocatorKind.Unspecified;
    /// <summary>
    /// Explicit native allocation symbol, or null when allocation uses the declared family.
    /// </summary>
    public string? allocatorFunction { get; set; }
    /// <summary>
    /// Optional managed conversion expressions accompanying the native lowering.
    /// </summary>
    public CppManagedProjection? managedProjection { get; set; }
    /// <summary>
    /// Native headers required to compile this conversion or invocation.
    /// </summary>
    public List<string> requiredHeaders { get; set; } = [];
    /// <summary>
    /// Evidence level checked against the selected lowering safety policy.
    /// </summary>
    public CppLoweringSafety safety { get; set; } = CppLoweringSafety.UserAsserted;
}
