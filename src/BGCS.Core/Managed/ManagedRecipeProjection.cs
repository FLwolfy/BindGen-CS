using BGCS.Core.Common;

namespace BGCS.Core.Managed;

/// <summary>Declares an explicit managed presentation without attaching syntax trees or backend services.</summary>
/// <param name="managedType">The managed public type name.</param>
/// <param name="managedToNativeExpression">The optional input conversion using the value placeholder.</param>
/// <param name="nativeToManagedExpression">The optional output conversion using the value placeholder.</param>
/// <param name="requiredNamespace">The optional namespace required by the type and conversion expressions.</param>
public sealed record ManagedRecipeProjection(
    string managedType,
    string? managedToNativeExpression = null,
    string? nativeToManagedExpression = null,
    string? requiredNamespace = null
);

/// <summary>Describes one named conversion helper independently of whether its native type matches an export.</summary>
/// <param name="id">The unique configured recipe identity.</param>
/// <param name="cAbiType">The explicitly requested native scalar or pointer carrier.</param>
/// <param name="projection">The managed type and conversion assertions.</param>
public sealed record ManagedRecipeHelper(
    string id,
    string cAbiType,
    ManagedRecipeProjection projection
);

/// <summary>Associates one detached ABI parameter identity with explicit managed presentation.</summary>
/// <param name="nativeParameter">The exact original input or original-result parameter identity.</param>
/// <param name="projection">The asserted managed type and conversion expressions.</param>
public sealed record ManagedValueProjection(
    StableId nativeParameter,
    ManagedRecipeProjection projection
);
