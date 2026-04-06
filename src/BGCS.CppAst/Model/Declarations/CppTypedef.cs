// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// A C++ typedef (e.g `typedef int XXX`)
/// </summary>
public sealed class CppTypedef : CppTypeDeclaration, ICppMemberWithVisibility, ICppAttributeContainer
{
    /// <summary>
    /// Creates a new instance of a typedef.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name">Name of the typedef (e.g `XXX`)</param>
    /// <param name="type">Underlying type.</param>
    public CppTypedef(CXCursor cursor, string name, CppType type) : base(cursor, CppTypeKind.Typedef)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ElementType = type;
        Attributes = [];
        TokenAttributes = [];
        MetaAttributes = new MetaAttributeMap();
    }

    /// <summary>
    /// Gets <c>Attributes</c>.
    /// </summary>
    public List<CppAttribute> Attributes { get; }

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    /// <summary>
    /// Gets <c>TokenAttributes</c>.
    /// </summary>
    public List<CppAttribute> TokenAttributes { get; }

    /// <summary>
    /// Gets or sets <c>MetaAttributes</c>.
    /// </summary>
    public MetaAttributeMap MetaAttributes { get; private set; }

    /// <summary>
    /// Gets <c>ElementType</c>.
    /// </summary>
    public CppType ElementType { get; }

    /// <summary>
    /// Visibility of this element.
    /// </summary>
    public CppVisibility Visibility { get; set; }

    /// <summary>
    /// Gets or sets the name of this type.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Exposes public member <c>FullName</c>.
    /// </summary>
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

    /// <inheritdoc />
    public override int SizeOf
    {
        get => ElementType.SizeOf;
        set => throw new InvalidOperationException("Cannot set the SizeOf a TypeDef. The SizeOf is determined by the underlying ElementType");
    }

    /// <inheritdoc />
    public override CppType GetCanonicalType()
    {
        return ElementType.GetCanonicalType();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"typedef {ElementType.GetDisplayName()} {Name}";
    }
}
