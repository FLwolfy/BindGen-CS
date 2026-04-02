// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// A C++ standard or scoped enum.
/// </summary>
public sealed class CppEnum : CppTypeDeclaration, ICppMemberWithVisibility, ICppAttributeContainer
{
    /// <summary>
    /// Creates a new instance of this enum.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name">Name of this enum</param>
    public CppEnum(CXCursor cursor, string name) : base(cursor, CppTypeKind.Enum)
    {
        Name = name;
        Items = new CppContainerList<CppEnumItem>(this);
        Attributes = [];
        TokenAttributes = [];
    }

    /// <inheritdoc />
    public CppVisibility Visibility { get; set; }

    /// <inheritdoc />
    public string Name { get; set; }

    public override string FullName
    {
        get
        {
            string fullparent = FullParentName;
            if (string.IsNullOrEmpty(fullparent))
            {
                return Name;
            }
            else
            {
                return $"{fullparent}::{Name}";
            }
        }
    }

    /// <summary>
    /// Gets or sets a boolean indicating if this enum is scoped.
    /// </summary>
    public bool IsScoped { get; set; }

    /// <summary>
    /// Gets or sets the underlying integer type of this enum.
    /// </summary>
    public CppType IntegerType { get; set; }

    /// <summary>
    /// Gets the definition of the enum items.
    /// </summary>
    public CppContainerList<CppEnumItem> Items { get; }

    public bool IsAnonymous { get; set; }

    /// <summary>
    /// Gets the list of attached attributes.
    /// </summary>
    public List<CppAttribute> Attributes { get; }

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    public List<CppAttribute> TokenAttributes { get; }

    public MetaAttributeMap MetaAttributes { get; private set; } = new MetaAttributeMap();

    /// <inheritdoc />
    public override int SizeOf
    {
        get => IntegerType?.SizeOf ?? 0;
        set => throw new InvalidOperationException("Cannot set the SizeOf an enum as it is determined only by the SizeOf of its underlying IntegerType");
    }

    /// <inheritdoc />
    public override CppType GetCanonicalType() => IntegerType;

    /// <inheritdoc />
    public override IEnumerable<ICppDeclaration> Children => Items;

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Visibility != CppVisibility.Default)
        {
            builder.Append(Visibility.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        builder.Append("enum ");
        if (IsScoped)
        {
            builder.Append("class ");
        }

        builder.Append(Name);

        if (IntegerType != null && !(IntegerType is CppPrimitiveType primitive && primitive.Kind == CppPrimitiveKind.Int))
        {
            builder.Append(": ");
            builder.Append(IntegerType.GetDisplayName());
        }

        builder.Append(" {...}");
        return builder.ToString();
    }
}
