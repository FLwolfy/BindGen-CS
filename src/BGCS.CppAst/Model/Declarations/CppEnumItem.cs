// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using System;
using System.Collections.Generic;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// An enum item of <see cref="CppEnum"/>.
/// </summary>
public sealed class CppEnumItem : CppDeclaration, ICppMember, ICppAttributeContainer
{
    /// <summary>
    /// Creates a new instance of this enum item.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name">Name of the enum item.</param>
    /// <param name="value">Associated value of this enum item.</param>
    public CppEnumItem(CXCursor cursor, string name, long value) : base(cursor)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
    }

    /// <inheritdoc />
    public string Name { get; set; }

    /// <summary>
    /// Gets the value of this enum item.
    /// </summary>
    public long Value { get; set; }

    /// <summary>
    /// Gets the value of this enum item as an expression.
    /// </summary>
    public CppExpression ValueExpression { get; set; }

    /// <inheritdoc />
    public List<CppAttribute> Attributes { get; } = [];

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    /// <summary>
    /// Gets <c>TokenAttributes</c>.
    /// </summary>
    public List<CppAttribute> TokenAttributes { get; } = [];

    /// <summary>
    /// Gets or sets <c>MetaAttributes</c>.
    /// </summary>
    public MetaAttributeMap MetaAttributes { get; private set; } = new MetaAttributeMap();

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name} = {ValueExpression}";
    }
}
