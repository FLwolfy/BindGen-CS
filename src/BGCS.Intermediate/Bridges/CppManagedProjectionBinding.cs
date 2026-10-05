namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Freezes a managed conversion whose native carrier is resolved from the generated C header's binding IR.
/// </summary>
/// <param name="aliasName">Exact native typedef identifying the carrier in the binding module.</param>
/// <param name="methodName">Managed conversion name suffix.</param>
/// <param name="managedType">Managed value type supplied by the lowering extension.</param>
/// <param name="managedToNativeExpression">Optional conversion expression containing the value placeholder.</param>
/// <param name="nativeToManagedExpression">Optional reverse conversion expression containing the value placeholder.</param>
/// <param name="requiredNamespace">Optional namespace required by the managed value type.</param>
public sealed record CppManagedProjectionBinding(
    string aliasName,
    string methodName,
    string managedType,
    string? managedToNativeExpression,
    string? nativeToManagedExpression,
    string? requiredNamespace
);
