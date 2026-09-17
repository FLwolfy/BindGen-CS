namespace BGCS.Intermediate;

/// <summary>
/// Identifies the semantic role of a type in the binding intermediate representation.
/// </summary>
public enum BindingTypeKind
{
    Primitive,
    Enumeration,
    Structure,
    Union,
    OpaqueHandle,
    FunctionPointer,
    Alias,
    Unexposed
}

/// <summary>
/// Describes a native-to-managed type reference after target ABI analysis.
/// </summary>
/// <param name="NativeName">Canonical native spelling.</param>
/// <param name="ManagedName">Managed spelling emitted by C# backends.</param>
/// <param name="PointerDepth">Number of native pointer or reference indirections.</param>
/// <param name="IsConst">Whether the first native indirection is const-qualified.</param>
/// <param name="Size">Native storage size in bytes, or zero when unsized.</param>
public sealed record BindingTypeReference(string NativeName, string ManagedName, int PointerDepth, bool IsConst, int Size);

/// <summary>
/// Describes one ABI-positioned field in a structure or union.
/// </summary>
/// <param name="NativeName">Native field name.</param>
/// <param name="ManagedName">Managed field name.</param>
/// <param name="Type">Analyzed field type.</param>
/// <param name="Offset">Byte offset from the start of the containing type.</param>
/// <param name="BitOffset">Bit offset from the start of the containing type.</param>
/// <param name="BitWidth">Bit width, or zero for a non-bitfield.</param>
/// <param name="ArrayDimensions">Fixed array dimensions from outermost to innermost.</param>
public sealed record BindingField(string NativeName, string ManagedName, BindingTypeReference Type, long Offset,
    long BitOffset, int BitWidth, IReadOnlyList<int> ArrayDimensions);

/// <summary>
/// Describes one named enumeration value.
/// </summary>
/// <param name="NativeName">Native enumerator name.</param>
/// <param name="ManagedName">Managed enumerator name.</param>
/// <param name="Value">Normalized integral expression.</param>
public sealed record BindingEnumMember(string NativeName, string ManagedName, string Value);

/// <summary>
/// Represents an ABI-analyzed native type independent of a concrete output language.
/// </summary>
public sealed class BindingType
{
    /// <summary>
    /// Initializes a binding type.
    /// </summary>
    public BindingType(string nativeName, string managedName, BindingTypeKind kind, int size, int alignment)
    {
        NativeName = nativeName;
        ManagedName = managedName;
        Kind = kind;
        Size = size;
        Alignment = alignment;
    }

    public string NativeName { get; }
    public string ManagedName { get; }
    public BindingTypeKind Kind { get; }
    public int Size { get; }
    public int Alignment { get; }
    public IList<BindingField> Fields { get; } = new List<BindingField>();
    public IList<BindingEnumMember> EnumMembers { get; } = new List<BindingEnumMember>();
}
