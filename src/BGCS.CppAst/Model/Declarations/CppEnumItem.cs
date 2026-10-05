// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// An enum item of <see cref = "CppEnum"/>.
/// </summary>
public sealed class CppEnumItem : CppDeclaration, ICppMember, ICppAttributeContainer
{
    /// <summary>
    /// Creates a mutable native enum item projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The non-null native item identifier.
    /// </param>
    /// <param name="value">
    /// The signed integral value evaluated by Clang.
    /// </param>
    public CppEnumItem(
        CXCursor cursor,
        string name,
        long value
    ) : base(cursor)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.value = value;
    }

    /// <inheritdoc/>
    public string name { get; set; }
    /// <summary>
    /// Gets the value of this enum item.
    /// </summary>
    public long value { get; set; }
    /// <summary>
    /// Gets the value of this enum item as an expression.
    /// </summary>
    public CppExpression? valueExpression { get; set; }
    /// <inheritdoc/>
    public List<CppAttribute> attributes { get; } = [];
    /// <summary>
    /// Gets mutable attributes recovered from source tokens outside the native attribute-cursor list.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this declaration.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; private set; } = new MetaAttributeMap();

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.name} = {this.valueExpression}";
    }
}
