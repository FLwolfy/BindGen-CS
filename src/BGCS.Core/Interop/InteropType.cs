using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Core.Interop;

/// <summary>Preserves a C ABI type and its storage facts without selecting a managed representation.</summary>
/// <param name="id">The stable ABI type identity.</param>
/// <param name="nativeName">The native type spelling.</param>
/// <param name="category">The native ABI shape.</param>
/// <param name="size">Native byte size, or null when unknown.</param>
/// <param name="alignment">Native alignment, or null when unknown.</param>
public sealed record InteropType(
    StableId id,
    string nativeName,
    NativeTypeCategory category,
    long? size,
    int? alignment
)
{
    /// <summary>Gets the detached original declaration or nearest source usage for actionable ABI diagnostics.</summary>
    public SourceLocation? source { get; init; }
    /// <summary>Gets the source declaration identity.</summary>
    public StableId? sourceDeclaration { get; init; }
    /// <summary>Gets the original source type identity when this C shape represents lowered source storage.</summary>
    public StableId? sourceNativeType { get; init; }
    /// <summary>Gets an explicit bridge-owned generated type identity, separate from original native type facts.</summary>
    public StableId? sourceGeneratedType { get; init; }
    /// <summary>Gets source qualification preserved through a bridge reparse; unnamed storage may retain only its logical owning scope, which is not a usable type declaration name.</summary>
    public string? sourceQualifiedName { get; init; }
    /// <summary>Gets finite checked value operations without changing this raw C storage carrier.</summary>
    public NativeValueProtocol? valueProtocol { get; init; }
    /// <summary>Gets the original named declaration, without output-language naming.</summary>
    public string? declarationName { get; init; }
    /// <summary>Gets the record role, including struct, union and opaque class.</summary>
    public string? recordKind { get; init; }
    /// <summary>Gets the scalar category.</summary>
    public string? primitiveKind { get; init; }
    /// <summary>Gets actual native scalar signedness when known.</summary>
    public bool? isSigned { get; init; }
    /// <summary>Gets one level of native qualification.</summary>
    public string? qualifier { get; init; }
    /// <summary>Gets the underlying element or alias type.</summary>
    public StableId? elementType { get; init; }
    /// <summary>Gets fixed array length, or null for an incomplete array.</summary>
    public long? arrayLength { get; init; }
    /// <summary>Gets storage fields in native declaration order.</summary>
    public ImmutableArray<InteropField> fields { get; init; } = [];
    /// <summary>Gets native enumerators in declaration order.</summary>
    public ImmutableArray<InteropEnumValue> enumValues { get; init; } = [];
    /// <summary>Gets native source documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets the return type of a callback type.</summary>
    public StableId? returnType { get; init; }
    /// <summary>Gets the callback parameter types in ABI order.</summary>
    public ImmutableArray<StableId> parameterTypes { get; init; } = [];
    /// <summary>Gets the callback calling convention.</summary>
    public string? callingConvention { get; init; }
    /// <summary>Gets whether the callback signature is variadic.</summary>
    public bool isVariadic { get; init; }
    /// <summary>Gets whether the callback declares an exact prototype.</summary>
    public bool hasPrototype { get; init; } = true;
}

/// <summary>Records native field storage independently of any managed carrier.</summary>
/// <param name="id">The source field identity.</param>
/// <param name="name">The native field name.</param>
/// <param name="type">The field type identity.</param>
/// <param name="offset">Native byte offset.</param>
/// <param name="bitOffset">Native bit offset.</param>
/// <param name="bitWidth">Bitfield width, or null for an ordinary field.</param>
public sealed record InteropField(
    StableId id,
    string name,
    StableId type,
    long? offset,
    long? bitOffset,
    int? bitWidth
)
{
    /// <summary>Gets detached source documentation without interpreting it as output-language markup.</summary>
    public string? documentation { get; init; }
}

/// <summary>Preserves both enum source expressions and independently evaluated values.</summary>
/// <param name="id">The enumerator identity.</param>
/// <param name="name">The native enumerator name.</param>
/// <param name="expression">The original source expression.</param>
/// <param name="evaluatedValue">The evaluated integral value.</param>
public sealed record InteropEnumValue(
    StableId id,
    string name,
    string? expression,
    string? evaluatedValue
)
{
    /// <summary>Gets detached source documentation without retaining parser comment nodes.</summary>
    public string? documentation { get; init; }
}
