namespace BGCS.Cpp2C.Lowering;

/// <summary>Optional managed projection metadata consumed by managed artifact contributors and tooling.</summary>
/// <param name="managedType">
/// Fully qualified managed carrier type.
/// </param>
/// <param name="managedToNativeExpression">
/// Optional managed-to-native expression, or null for no projection.
/// </param>
/// <param name="nativeToManagedExpression">
/// Optional native-to-managed expression, or null for no projection.
/// </param>
/// <param name="requiredNamespace">
/// Optional namespace required by the projection expressions.
/// </param>
public sealed record CppManagedProjection(
    string managedType,
    string? managedToNativeExpression = null,
    string? nativeToManagedExpression = null,
    string? requiredNamespace = null
);
