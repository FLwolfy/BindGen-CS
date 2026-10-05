// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ standard or scoped enum.
/// </summary>
public sealed class CppEnum : CppTypeDeclaration, ICppMemberWithVisibility, ICppAttributeContainer
{
    /// <summary>
    /// Creates a mutable native enumeration projection with empty owned child collections.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The native declaration identifier, empty for an unnamed declaration.
    /// </param>
    public CppEnum(
        CXCursor cursor,
        string name
    ) : base(cursor, CppTypeKind.Enum)
    {
        this.name = name;
        this.items = new CppContainerList<CppEnumItem>(this);
        this.attributes = [];
        this.integerType = CppPrimitiveType.@int;
    }

    /// <inheritdoc/>
    public CppVisibility visibility { get; set; }
    /// <inheritdoc/>
    public string name { get; set; }

    /// <summary>
    /// Gets the current enum identifier qualified by enclosing classes and non-inline namespaces.
    /// </summary>
    public override string fullName
    {
        get
        {
            string fullparent = this.fullParentName;
            if (string.IsNullOrEmpty(fullparent))
            {
                return this.name;
            }
            else
            {
                return $"{fullparent}::{this.name}";
            }
        }
    }

    /// <summary>
    /// Gets or sets a boolean indicating if this enum is scoped.
    /// </summary>
    public bool isScoped { get; set; }
    /// <summary>
    /// Gets or sets the underlying integer type of this enum.
    /// </summary>
    public CppType integerType { get; set; }
    /// <summary>
    /// Gets the definition of the enum items.
    /// </summary>
    public CppContainerList<CppEnumItem> items { get; }
    /// <summary>
    /// Gets or sets whether the native enum declaration lacks an explicit identifier.
    /// </summary>
    public bool isAnonymous { get; set; }
    /// <summary>
    /// Gets the list of attached attributes.
    /// </summary>
    public List<CppAttribute> attributes { get; }
    /// <summary>
    /// Gets mutable attributes recovered from source tokens outside the native attribute-cursor list.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this declaration.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; private set; } = new MetaAttributeMap();
    /// <inheritdoc/>
    public override int sizeOf { get => this.integerType?.sizeOf ?? 0; set => throw new InvalidOperationException("Cannot set the SizeOf an enum as it is determined only by the SizeOf of its underlying IntegerType"); }

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this.integerType;
    /// <inheritdoc/>
    public override IEnumerable<ICppDeclaration> children => this.items;

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (this.visibility != CppVisibility.Default)
        {
            builder.Append(this.visibility.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        builder.Append("enum ");
        if (this.isScoped)
        {
            builder.Append("class ");
        }

        builder.Append(this.name);
        if (this.integerType != null && !(this.integerType is CppPrimitiveType primitive && primitive.kind == CppPrimitiveKind.Int))
        {
            builder.Append(": ");
            builder.Append(this.integerType.GetDisplayName());
        }

        builder.Append(" {...}");
        return builder.ToString();
    }
}
