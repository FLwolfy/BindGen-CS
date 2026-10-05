// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ typedef (e.g `typedef int XXX`)
/// </summary>
public sealed class CppTypedef : CppTypeDeclaration, ICppMemberWithVisibility, ICppAttributeContainer
{
    /// <summary>
    /// Creates a mutable native type alias projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The non-null native typedef identifier.
    /// </param>
    /// <param name="type">
    /// The borrowed aliased native type.
    /// </param>
    public CppTypedef(
        CXCursor cursor,
        string name,
        CppType type
    ) : base(cursor, CppTypeKind.Typedef)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.elementType = type;
        this.attributes = [];
        this.metaAttributes = new MetaAttributeMap();
    }

    /// <summary>
    /// Gets mutable native attributes attached to this typedef declaration.
    /// </summary>
    public List<CppAttribute> attributes { get; }
    /// <summary>
    /// Gets mutable attributes recovered from source tokens outside the native attribute-cursor list.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this declaration.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; private set; }
    /// <summary>
    /// Gets the borrowed native type represented by this typedef.
    /// </summary>
    public CppType elementType { get; }
    /// <summary>
    /// Visibility of this element.
    /// </summary>
    public CppVisibility visibility { get; set; }
    /// <summary>
    /// Gets or sets the unqualified native typedef identifier.
    /// </summary>
    public string name { get; set; }

    /// <summary>
    /// Gets the current typedef identifier qualified by its enclosing classes and non-inline namespaces.
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

    /// <inheritdoc/>
    public override int sizeOf { get => this.elementType.sizeOf; set => throw new InvalidOperationException("Cannot set the SizeOf a TypeDef. The SizeOf is determined by the underlying ElementType"); }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        return this.elementType.GetCanonicalType();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"typedef {this.elementType.GetDisplayName()} {this.name}";
    }
}
