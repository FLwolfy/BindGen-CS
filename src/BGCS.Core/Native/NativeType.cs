using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Native;

/// <summary>Identifies a source-language type shape without a managed carrier.</summary>
public enum NativeTypeCategory
{
    /// <summary>A target-sized native scalar.</summary>
    Primitive,
    /// <summary>An unmanaged pointer.</summary>
    Pointer,
    /// <summary>A source-language reference.</summary>
    Reference,
    /// <summary>A qualified underlying type.</summary>
    Qualified,
    /// <summary>A fixed or incomplete array.</summary>
    Array,
    /// <summary>A class, struct, union or Objective-C record.</summary>
    Record,
    /// <summary>An enumeration.</summary>
    Enumeration,
    /// <summary>A named alias.</summary>
    Alias,
    /// <summary>A callable type.</summary>
    Function,
    /// <summary>A template parameter or argument.</summary>
    Template,
    /// <summary>A shape whose complete representation is unavailable.</summary>
    Unexposed,
    /// <summary>An Objective-C block object with its own invocation and lifetime ABI.</summary>
    Block
}

/// <summary>Stores detached native type facts with references expressed as stable identities.</summary>
/// <param name="id">The stable type identity.</param>
/// <param name="category">The native type shape.</param>
/// <param name="spelling">The source-language spelling, including qualifiers.</param>
/// <param name="size">The target byte size, or null when unknown.</param>
/// <param name="alignment">The target byte alignment, or null when unknown.</param>
public sealed record NativeType(
    StableId id,
    NativeTypeCategory category,
    string spelling,
    long? size,
    int? alignment
)
{
    /// <summary>Gets the underlying type identity for an element-bearing shape.</summary>
    public StableId? elementType { get; init; }
    /// <summary>Gets the canonical type identity without erasing alias identity.</summary>
    public StableId? canonicalType { get; init; }
    /// <summary>Gets the associated declaration identity when the type is named.</summary>
    public StableId? declaration { get; init; }
    /// <summary>Gets the native primitive category; null denotes a nonscalar or unknown category.</summary>
    public string? primitiveKind { get; init; }
    /// <summary>Gets native integer signedness from the actual compiler type, or null when not applicable or unknown.</summary>
    public bool? isSigned { get; init; }
    /// <summary>Gets the qualifier at this level, independently of nested pointer qualifiers.</summary>
    public string? qualifier { get; init; }
    /// <summary>Gets the fixed array length, or null for an incomplete array.</summary>
    public long? arrayLength { get; init; }
    /// <summary>Gets whether the reference is an rvalue reference.</summary>
    public bool isRValueReference { get; init; }
    /// <summary>Gets the return type identity of a callable type.</summary>
    public StableId? returnType { get; init; }
    /// <summary>Gets immutable callable parameter type identities.</summary>
    public ImmutableArray<StableId> parameterTypes { get; init; } = [];
    /// <summary>Gets the native calling convention, without an import-mode assumption.</summary>
    public string? callingConvention { get; init; }
    /// <summary>Gets whether the callable has an ellipsis parameter.</summary>
    public bool isVariadic { get; init; }
    /// <summary>Gets whether a callable declares an exact prototype rather than a C unspecified argument list.</summary>
    public bool hasPrototype { get; init; } = true;
}
