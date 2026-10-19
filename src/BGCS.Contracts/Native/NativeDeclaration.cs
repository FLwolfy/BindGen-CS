using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Native;

/// <summary>Stores a detached declaration and its immutable source-language facts.</summary>
/// <param name="id">The stable declaration identity.</param>
/// <param name="kind">The native declaration category.</param>
/// <param name="name">The native unqualified name.</param>
/// <param name="qualifiedName">The source-language qualified name.</param>
/// <param name="type">The declared type, or null for typeless declarations.</param>
/// <param name="source">The logical source location.</param>
public sealed record NativeDeclaration(
    StableId id,
    string kind,
    string name,
    string qualifiedName,
    StableId? type,
    SourceLocation source
)
{
    /// <summary>Gets the owning declaration, or null for a module-level declaration.</summary>
    public StableId? parent { get; init; }
    /// <summary>Gets immutable child declaration identities in native declaration order.</summary>
    public ImmutableArray<StableId> children { get; init; } = [];
    /// <summary>Gets source documentation detached from native comment handles.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets detached parsed documentation sections before managed parameter naming and bridge projection.</summary>
    public SourceDocumentation? documentationContent { get; init; }
    /// <summary>Gets a parameter's written source type before native array/function parameter adjustment; this is documentation, not its calling ABI.</summary>
    public string? sourceTypeSpelling { get; init; }
    /// <summary>Gets a parameter's typed written form, including array dimensions, separately from its adjusted calling-ABI type.</summary>
    public StableId? sourceType { get; init; }
    /// <summary>Gets the native visibility.</summary>
    public string? visibility { get; init; }
    /// <summary>Gets whether the compiler classifies this declaration as originating in a system header.</summary>
    public bool isSystemDeclaration { get; init; }
    /// <summary>Gets whether this declaration supplies a definition.</summary>
    public bool isDefinition { get; init; }
    /// <summary>Gets a record, callable or template subcategory.</summary>
    public string? role { get; init; }
    /// <summary>Gets the field byte offset, or null when not applicable or unknown.</summary>
    public long? offset { get; init; }
    /// <summary>Gets the compiler-established field slot size in bytes, independently of a reference type's referent size; null means unknown or not applicable.</summary>
    public long? storageSize { get; init; }
    /// <summary>Gets compiler provenance for the field slot size; this does not certify another consumer compiler.</summary>
    public Evidence? storageEvidence { get; init; }
    /// <summary>Gets the field bit offset, or null when not applicable or unknown.</summary>
    public long? bitOffset { get; init; }
    /// <summary>Gets the bitfield width, or null for an ordinary field.</summary>
    public int? bitWidth { get; init; }
    /// <summary>Gets a constant expression or default argument without evaluating it as C#.</summary>
    public string? value { get; init; }
    /// <summary>Gets the return type identity of a callable declaration.</summary>
    public StableId? returnType { get; init; }
    /// <summary>Gets whether a callable is variadic.</summary>
    public bool isVariadic { get; init; }
    /// <summary>Gets whether a callable declares an exact native prototype.</summary>
    public bool hasPrototype { get; init; } = true;
    /// <summary>Gets compiler-established callable addressability, independently of native linker visibility.</summary>
    public NativeCallableAddressability addressability { get; init; }
    /// <summary>Gets the frontend procedure that established addressability; this does not certify a different native compiler.</summary>
    public Evidence? addressabilityEvidence { get; init; }
    /// <summary>Gets whether a callable is static.</summary>
    public bool isStatic { get; init; }
    /// <summary>Gets whether the compiler classifies the callable as inline.</summary>
    public bool isInline { get; init; }
    /// <summary>Gets the native linker visibility separately from C or C++ language linkage.</summary>
    public NativeLinkageKind nativeLinkage { get; init; }
    /// <summary>Gets whether a callable is explicitly deleted and cannot be invoked.</summary>
    public bool isDeleted { get; init; }
    /// <summary>Gets the compiler-independent member reference qualification, preserving an unresolved fact explicitly.</summary>
    public NativeReferenceQualifier referenceQualifier { get; init; }
    /// <summary>Gets the native language linkage independently of an export strategy.</summary>
    public string? linkage { get; init; }
    /// <summary>Gets whether a callable is const-qualified.</summary>
    public bool isConst { get; init; }
    /// <summary>Gets whether a source member callable is volatile-qualified.</summary>
    public bool isVolatile { get; init; }
    /// <summary>Gets the compiler-evaluated nonthrowing callable contract, or null when unresolved.</summary>
    public bool? isNoThrow { get; init; }
    /// <summary>Gets whether a callable is virtual.</summary>
    public bool isVirtual { get; init; }
    /// <summary>Gets whether a record or callable is abstract.</summary>
    public bool isAbstract { get; init; }
    /// <summary>Gets whether the record has a complete native definition.</summary>
    public bool isCompleteDefinition { get; init; }
    /// <summary>Gets compiler-evaluated default constructibility, or null when it has not been established.</summary>
    public bool? isDefaultConstructible { get; init; }
    /// <summary>Gets compiler-evaluated standard-layout classification, or null when it has not been established.</summary>
    public bool? isStandardLayout { get; init; }
    /// <summary>Gets compiler-evaluated trivial copyability, or null when it has not been established.</summary>
    public bool? isTriviallyCopyable { get; init; }
    /// <summary>Gets the native calling convention.</summary>
    public string? callingConvention { get; init; }
    /// <summary>Gets immutable native attribute spellings.</summary>
    public ImmutableArray<string> attributes { get; init; } = [];
    /// <summary>Gets immutable native base-class contracts.</summary>
    public ImmutableArray<NativeBaseClass> baseClasses { get; init; } = [];
    /// <summary>Gets immutable template parameters and specialization arguments.</summary>
    public ImmutableArray<NativeTemplateArgument> templateArguments { get; init; } = [];
    /// <summary>Gets declared template parameter type identities in source order.</summary>
    public ImmutableArray<StableId> templateParameters { get; init; } = [];
    /// <summary>Gets whether the declaration is a primary, partial or specialized template.</summary>
    public string? templateKind { get; init; }
    /// <summary>Gets the independently evaluated integral value, preserving the original expression separately.</summary>
    public string? evaluatedIntegralValue { get; init; }
    /// <summary>Gets a compiler-evaluated scalar value, or null when no supported constant representation is established.</summary>
    public NativeScalarConstant? constantValue { get; init; }
    /// <summary>Gets exact compiler-established string literal units, independently of scalar values and pointer ABI.</summary>
    public NativeStringConstant? stringConstant { get; init; }
    /// <summary>Gets the exact free function used as an immutable variable initializer, independently of the variable's pointer type.</summary>
    public StableId? functionReference { get; init; }
    /// <summary>Gets compiler reference-expression provenance for an immutable variable initializer.</summary>
    public Evidence? functionReferenceEvidence { get; init; }
    /// <summary>Gets detached macro argument and active-definition facts.</summary>
    public NativeMacroDefinition? macro { get; init; }
}

/// <summary>Preserves a base-class identity and conversion-relevant inheritance facts.</summary>
/// <param name="type">The base type identity.</param>
/// <param name="visibility">The native access level.</param>
/// <param name="isVirtual">Whether the base is inherited virtually.</param>
public sealed record NativeBaseClass(
    StableId type,
    string visibility,
    bool isVirtual
);

/// <summary>Preserves an explicit template argument without retaining parser handles.</summary>
/// <param name="kind">The native argument category.</param>
/// <param name="spelling">The source-language argument spelling.</param>
/// <param name="type">The associated type identity, or null for nontype arguments.</param>
public sealed record NativeTemplateArgument(
    string kind,
    string spelling,
    StableId? type
);
