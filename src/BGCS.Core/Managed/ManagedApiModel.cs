using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Interop;

namespace BGCS.Core.Managed;

/// <summary>Identifies the lifetime scope exposed by a managed library API.</summary>
public enum ManagedApiStyle
{
    /// <summary>An independently owned native library context.</summary>
    Instance,
    /// <summary>An explicitly shared static library context.</summary>
    Static
}

/// <summary>Preserves managed API names and semantic identities before any C# source syntax is chosen.</summary>
/// <param name="namespace">The managed namespace.</param>
/// <param name="name">The public API context name.</param>
/// <param name="style">The public context lifetime style.</param>
/// <param name="interop">The verified native ABI backing the API.</param>
/// <param name="types">Managed type names keyed by ABI identity.</param>
/// <param name="methods">Public methods keyed by native export identity.</param>
public sealed record ManagedApiModel(
    string @namespace,
    string name,
    ManagedApiStyle style,
    InteropModule interop,
    ImmutableArray<ManagedType> types,
    ImmutableArray<ManagedMember> methods
)
{
    /// <summary>Gets whether canonical raw/native call signatures carry checked native name/type attributes.</summary>
    public bool emitNativeMetadata { get; init; }
    /// <summary>Gets explicit non-owning address carriers; their native pointers remain unchanged.</summary>
    public ImmutableArray<ManagedPointerCarrier> pointerCarriers { get; init; } = [];
    /// <summary>Gets finite callable address presentation without changing lifetime evidence.</summary>
    public ImmutableArray<ManagedPointerFunction> pointerFunctions { get; init; } = [];
    /// <summary>Gets whether generated storage, enums and ABI delegates belong to the public API type.</summary>
    public bool nestGeneratedTypesInApi { get; init; }
    /// <summary>Gets whether the backend fills missing source documentation without changing native facts.</summary>
    public bool emitPlaceholderComments { get; init; }
    /// <summary>Gets whether selected callback typedefs expose named raw ABI delegate declarations independently of callback lifetime wrappers.</summary>
    public bool emitCallbackDelegates { get; init; } = true;
    /// <summary>Gets whether each public context/facade exposes its configured NativeLibraryName constant.</summary>
    public bool emitLibraryNameConstant { get; init; } = true;
    /// <summary>Gets an optional qualified external constant for static imports, independent of native calling ABI.</summary>
    public string? importLibraryNameConstant { get; init; }
    /// <summary>Gets explicitly declared conversion helpers, including configured rules not matched by an export.</summary>
    public ImmutableArray<ManagedRecipeHelper> recipeHelpers { get; init; } = [];
    /// <summary>Gets explicit managed enum declarations whose conversions preserve separate native raw carriers.</summary>
    public ImmutableArray<ManagedEnumDefinition> enums { get; init; } = [];
    /// <summary>Gets selected managed names for typed native source constants.</summary>
    public ImmutableArray<ManagedConstant> constants { get; init; } = [];
    /// <summary>Gets additional managed names forwarding to existing checked operations.</summary>
    public ImmutableArray<ManagedFunctionAlias> aliases { get; init; } = [];
    /// <summary>Gets named context-bound views sharing existing friendly operations and their resource lifetimes.</summary>
    public ImmutableArray<ManagedFunctionContainer> functionContainers { get; init; } = [];
    /// <summary>Gets the maximum finite default-argument overloads per context or function view.</summary>
    public int defaultOverloadLimit { get; init; } = 128;
    /// <summary>Gets explicit field-copy projections whose consumer types never replace the generated native ABI carrier.</summary>
    public ImmutableArray<ExternalTypeProjection> externalTypeProjections { get; init; } = [];
}

/// <summary>Names a compiler-established native scalar value without embedding backend syntax.</summary>
/// <param name="sourceDeclaration">The exact constant identity in the interoperability module.</param>
/// <param name="name">The projected managed identifier.</param>
public sealed record ManagedConstant(
    StableId sourceDeclaration,
    string name
)
{
    /// <summary>Gets optional asserted managed presentation without modifying native source facts.</summary>
    public ManagedConstantPresentation? presentation { get; init; }
}

/// <summary>Describes a managed type's identity and name without storing generated source.</summary>
/// <param name="nativeType">The source ABI type identity.</param>
/// <param name="name">The managed declared name.</param>
/// <param name="frameworkType">The framework type identity for a scalar, or null for a declared type.</param>
public sealed record ManagedType(
    StableId nativeType,
    string name,
    string? frameworkType
)
{
    /// <summary>Gets existing native enum member presentation without changing the underlying ABI.</summary>
    public ManagedNativeEnumPresentation? enumPresentation { get; init; }
    /// <summary>Gets whether an exact source or stable-identity mapping selected this name, including an inline-array carrier.</summary>
    public bool isNameExplicit { get; init; }
    /// <summary>Gets optional carrier naming documentation and value conveniences without changing native storage.</summary>
    public ManagedRecordPresentation? recordPresentation { get; init; }
    /// <summary>Gets explicit field and enumerator names keyed by original native declaration identity.</summary>
    public ImmutableArray<ManagedMemberName> memberNames { get; init; } = [];
}

/// <summary>Names a native field or enumerator without using output syntax or a serializer-specific dictionary key.</summary>
/// <param name="nativeDeclaration">The original native member identity.</param>
/// <param name="name">The selected managed identifier.</param>
public sealed record ManagedMemberName(
    StableId nativeDeclaration,
    string name
);

/// <summary>Describes a public managed method before import and cleanup planning.</summary>
/// <param name="nativeExport">The source ABI export identity.</param>
/// <param name="name">The managed method name.</param>
/// <param name="parameterNames">Managed parameter names in native ABI order.</param>
public sealed record ManagedMember(
    StableId nativeExport,
    string name,
    ImmutableArray<string> parameterNames
)
{
    /// <summary>Gets alternate public value input signatures; these do not duplicate or change raw imports.</summary>
    public ImmutableArray<ManagedInputVariation> inputVariations { get; init; } = [];
    /// <summary>Gets an explicitly selected C receiver; null retains the ordinary context operation.</summary>
    public ManagedReceiver? receiver { get; init; }
    /// <summary>Gets inert replacement documentation, or null to retain source documentation.</summary>
    public string? documentationOverride { get; init; }
    /// <summary>Gets explicit constant arguments keyed by original parameter identity.</summary>
    public ImmutableArray<ManagedDefaultArgument> defaults { get; init; } = [];
    /// <summary>Gets parameters whose explicit native contracts support a managed string input.</summary>
    public ImmutableArray<StableId> stringInputs { get; init; } = [];
    /// <summary>Gets explicitly bounded synchronous buffers projected as pinned managed spans.</summary>
    public ImmutableArray<StableId> bufferInputs { get; init; } = [];
    /// <summary>Gets explicitly directed nonretaining single-value pointers projected through checked native temporary storage.</summary>
    public ImmutableArray<StableId> valuePointers { get; init; } = [];
    /// <summary>Gets explicit managed value conversions keyed by exact native input or original-result channel identity.</summary>
    public ImmutableArray<ManagedValueProjection> valueProjections { get; init; } = [];
    /// <summary>Gets an explicit direct-return managed conversion for a C ABI without a status result channel.</summary>
    public ManagedRecipeProjection? returnProjection { get; init; }
}
