using System.Collections.Generic;

namespace BGCS.Intermediate;

/// <summary>
/// Identifies the semantic role of a type in the binding intermediate representation.
/// </summary>
public enum BindingTypeKind
{
    /// <summary>
    /// A target ABI scalar type.
    /// </summary>
    Primitive,
    /// <summary>
    /// A named set of integral values.
    /// </summary>
    Enumeration,
    /// <summary>
    /// A record with sequentially positioned fields.
    /// </summary>
    Structure,
    /// <summary>
    /// A record whose fields share storage.
    /// </summary>
    Union,
    /// <summary>
    /// An incomplete native object accessed through its identity or pointer.
    /// </summary>
    OpaqueHandle,
    /// <summary>
    /// An unmanaged callable address.
    /// </summary>
    FunctionPointer,
    /// <summary>
    /// A named reference to another analyzed type.
    /// </summary>
    Alias,
    /// <summary>
    /// A declaration whose detailed representation is unavailable.
    /// </summary>
    Unexposed
}

/// <summary>
/// Describes a native-to-managed type reference after target ABI analysis.
/// </summary>
/// <param name = "nativeName">Canonical native spelling.</param>
/// <param name = "managedName">Managed spelling emitted by C# backends.</param>
/// <param name = "pointerDepth">Number of native pointer or reference indirections.</param>
/// <param name = "isConst">Whether the first native indirection is const-qualified.</param>
/// <param name = "size">Native storage size in bytes, or zero when unsized.</param>
public sealed record BindingTypeReference(
    string nativeName,
    string managedName,
    int pointerDepth,
    bool isConst,
    int size
);
/// <summary>
/// Records target-specific ABI evidence for a managed carrier supplied outside generated source.
/// </summary>
/// <param name = "nativeTypes">Native aliases represented by the carrier.</param>
/// <param name = "managedType">Managed carrier type name.</param>
/// <param name = "size">Declared carrier size in bytes.</param>
/// <param name = "alignment">Declared carrier alignment in bytes.</param>
/// <param name = "allowsByValue">Whether the contract accepts ABI by-value positions.</param>
/// <param name = "layoutValidationBypassed">Whether native layout matching was explicitly bypassed.</param>
public sealed record BindingExternalTypeContract(
    IReadOnlyList<string> nativeTypes,
    string managedType,
    int size,
    int alignment,
    bool allowsByValue,
    bool layoutValidationBypassed
)
{
    private IReadOnlyList<string> m_values = BindingCollection.Copy(nativeTypes);
    /// <summary>
    /// Gets or initializes a frozen copy of the analyzed values.
    /// </summary>
    public IReadOnlyList<string> nativeTypes { get => m_values; init => m_values = BindingCollection.Copy(value); }
}

/// <summary>
/// Describes one ABI-positioned field in a structure or union.
/// </summary>
/// <param name = "nativeName">Native field name.</param>
/// <param name = "managedName">Managed field name.</param>
/// <param name = "type">Analyzed field type.</param>
/// <param name = "offset">Byte offset from the start of the containing type.</param>
/// <param name = "bitOffset">Bit offset from the start of the containing type.</param>
/// <param name = "bitWidth">Bit width, or zero for a non-bitfield.</param>
/// <param name = "arrayDimensions">Fixed array dimensions from outermost to innermost.</param>
/// <param name="isBitField">Whether the declaration uses a native bitfield.</param>
/// <param name="isSignedBitField">Whether bitfield extraction must sign-extend the value.</param>
public sealed record BindingField(
    string nativeName,
    string managedName,
    BindingTypeReference type,
    long offset,
    long bitOffset,
    int bitWidth,
    IReadOnlyList<int> arrayDimensions,
    bool isBitField = false,
    bool isSignedBitField = false
)
{
    private IReadOnlyList<int> m_values = BindingCollection.Copy(arrayDimensions);
    /// <summary>
    /// Gets or initializes a frozen copy of the analyzed values.
    /// </summary>
    public IReadOnlyList<int> arrayDimensions { get => m_values; init => m_values = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets optional native field documentation.
    /// </summary>
    public string? comment { get; init; }
}

/// <summary>
/// Describes one named enumeration value.
/// </summary>
/// <param name = "nativeName">Native enumerator name.</param>
/// <param name = "managedName">Managed enumerator name.</param>
/// <param name = "value">Normalized integral expression.</param>
public sealed record BindingEnumMember(
    string nativeName,
    string managedName,
    string value
)
{
    private IReadOnlyList<string> m_attributes = [];
    /// <summary>
    /// Gets optional native enumerator documentation.
    /// </summary>
    public string? comment { get; init; }
    /// <summary>
    /// Gets frozen managed attribute expressions for this enumerator.
    /// </summary>
    public IReadOnlyList<string> attributes { get => m_attributes; init => m_attributes = BindingCollection.Copy(value); }
}

/// <summary>Describes a sentinel-backed validity property on a native value type.</summary>
/// <param name="fieldName">Managed sentinel field name.</param>
/// <param name="invalidValue">Sentinel expression representing an invalid value.</param>
/// <param name="propertyName">Generated validity property name.</param>
public sealed record BindingValidity(
    string fieldName,
    string invalidValue,
    string propertyName
);
/// <summary>
/// Represents an ABI-analyzed native type independent of a concrete output language.
/// </summary>
public sealed record BindingType
{
    private IReadOnlyList<string> m_attributes = [];
    private IReadOnlyList<BindingType> m_nestedTypes = [];
    private IReadOnlyList<BindingField> m_fields = [];
    private IReadOnlyList<BindingEnumMember> m_enumMembers = [];
    /// <summary>
    /// Captures the semantic identity and native layout of one type.
    /// </summary>
    /// <param name="nativeName">
    /// Canonical native declaration name.
    /// </param>
    /// <param name="managedName">
    /// Generated managed type name.
    /// </param>
    /// <param name="kind">
    /// Native semantic category.
    /// </param>
    /// <param name="size">
    /// Target storage size in bytes.
    /// </param>
    /// <param name="alignment">
    /// Target alignment in bytes.
    /// </param>
    public BindingType(
        string nativeName,
        string managedName,
        BindingTypeKind kind,
        int size,
        int alignment
    ) {
        this.nativeName = nativeName;
        this.managedName = managedName;
        this.kind = kind;
        this.size = size;
        this.alignment = alignment;
    }

    /// <summary>
    /// Gets the canonical native declaration name.
    /// </summary>
    public string nativeName { get; }
    /// <summary>
    /// Gets the managed type name selected by naming analysis.
    /// </summary>
    public string managedName { get; }
    /// <summary>
    /// Gets the native semantic category used for lowering and emission.
    /// </summary>
    public BindingTypeKind kind { get; }
    /// <summary>
    /// Gets analyzed target storage size in bytes, or zero for an unsized declaration.
    /// </summary>
    public int size { get; }
    /// <summary>
    /// Gets analyzed target alignment in bytes, or zero when unavailable.
    /// </summary>
    public int alignment { get; }
    /// <summary>
    /// Gets optional native declaration documentation.
    /// </summary>
    public string? comment { get; init; }
    /// <summary>
    /// Gets frozen managed attribute expressions for this declaration.
    /// </summary>
    public IReadOnlyList<string> attributes { get => m_attributes; init => m_attributes = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets whether the consumer supplies a custom definition for this type.
    /// </summary>
    public bool isCustomDefinition { get; init; }
    /// <summary>
    /// Gets the optional sentinel-backed validity property contract.
    /// </summary>
    public BindingValidity? validity { get; init; }
    /// <summary>
    /// Gets whether this type preserves only native size and alignment because its field definition was unavailable.
    /// Such storage is safe behind pointers but cannot be passed by value without target-specific ABI classification.
    /// </summary>
    public bool isOpaqueStorage { get; init; }
    /// <summary>Resolved target of an alias, or <see langword="null"/> for non-alias types.</summary>
    public BindingTypeReference? underlyingType { get; init; }
    /// <summary>Types declared inside this native type, preserving their managed ownership scope.</summary>
    public IReadOnlyList<BindingType> nestedTypes { get => m_nestedTypes; init => m_nestedTypes = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen fields with native offsets and bitfield layout.
    /// </summary>
    public IReadOnlyList<BindingField> fields { get => m_fields; init => m_fields = BindingCollection.Copy(value); }
    /// <summary>
    /// Gets frozen named values when this declaration is an enumeration.
    /// </summary>
    public IReadOnlyList<BindingEnumMember> enumMembers { get => m_enumMembers; init => m_enumMembers = BindingCollection.Copy(value); }
}
