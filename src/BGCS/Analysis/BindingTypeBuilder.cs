using System.Collections.Generic;
using System.Linq;
using BGCS.Intermediate;

namespace BGCS.Analysis;

internal sealed class BindingTypeBuilder
{
    /// <summary>
    /// Initializes a binding type.
    /// </summary>
    internal BindingTypeBuilder(
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

    public string nativeName { get; }
    public string managedName { get; }
    public BindingTypeKind kind { get; }
    public int size { get; }
    public int alignment { get; }
    public string? comment { get; set; }
    public List<string> attributes { get; set; } = new List<string>();
    public bool isCustomDefinition { get; set; }
    public BindingValidity? validity { get; set; }
    /// <summary>
    /// Gets whether this type preserves only native size and alignment because its field definition was unavailable.
    /// Such storage is safe behind pointers but cannot be passed by value without target-specific ABI classification.
    /// </summary>
    public bool isOpaqueStorage { get; set; }
    /// <summary>Resolved target of an alias, or <see langword="null"/> for non-alias types.</summary>
    public BindingTypeReference? underlyingType { get; set; }
    /// <summary>Types declared inside this native type, preserving their managed ownership scope.</summary>
    public List<BindingType> nestedTypes { get; set; } = new List<BindingType>();
    public List<BindingField> fields { get; set; } = new List<BindingField>();
    public List<BindingEnumMember> enumMembers { get; set; } = [];

    internal BindingType Freeze()
    {
        return new(this.nativeName, this.managedName, this.kind, this.size, this.alignment)
        {
            comment = this.comment,
            attributes = this.attributes,
            isCustomDefinition = this.isCustomDefinition,
            validity = this.validity,
            isOpaqueStorage = this.isOpaqueStorage,
            underlyingType = this.underlyingType,
            nestedTypes = this.nestedTypes,
            fields = this.fields,
            enumMembers = this.enumMembers
        };
    }

    internal static BindingTypeBuilder From(BindingType model)
    {
        return new(model.nativeName, model.managedName, model.kind, model.size, model.alignment)
        {
            comment = model.comment,
            attributes = model.attributes.ToList(),
            isCustomDefinition = model.isCustomDefinition,
            validity = model.validity,
            isOpaqueStorage = model.isOpaqueStorage,
            underlyingType = model.underlyingType,
            nestedTypes = model.nestedTypes.ToList(),
            fields = model.fields.ToList(),
            enumMembers = model.enumMembers.ToList()
        };
    }

    public static implicit operator BindingType(BindingTypeBuilder builder) => builder.Freeze();
}
