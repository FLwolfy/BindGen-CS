// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// A C++ field (of a struct/class) or global variable.
/// </summary>
public sealed class CppField : CppDeclaration, ICppMemberWithVisibility, ICppAttributeContainer
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppField"/>.
    /// </summary>
    public CppField(CXCursor cursor, CppType type, string name) : base(cursor)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = name;
        Attributes = [];
        TokenAttributes = [];
    }

    /// <inheritdoc />
    public CppVisibility Visibility { get; set; }

    /// <summary>
    /// Gets or sets the storage qualifier of this field/variable.
    /// </summary>
    public CppStorageQualifier StorageQualifier { get; set; }

    /// <summary>
    /// Gets attached attributes. Might be null.
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
    public MetaAttributeMap MetaAttributes { get; private set; } = new MetaAttributeMap();

    /// <summary>
    /// Gets the type of this field/variable.
    /// </summary>
    public CppType Type { get; set; }

    /// <inheritdoc />
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating if this field was created from an anonymous type
    /// </summary>
    public bool IsAnonymous { get; set; }

    /// <summary>
    /// Gets the associated init value (either an integer or a string...)
    /// </summary>
    public CppValue? InitValue { get; set; }

    /// <summary>
    /// Gets the associated init value as an expression.
    /// </summary>
    public CppExpression? InitExpression { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating that this field is a bit field. See <see cref="BitFieldWidth"/> to get the width of this field if <see cref="IsBitField"/> is <c>true</c>
    /// </summary>
    public bool IsBitField { get; set; }

    /// <summary>
    /// Gets or sets the number of bits for this bit field. Only valid if <see cref="IsBitField"/> is <c>true</c>.
    /// </summary>
    public int BitFieldWidth { get; set; }

    /// <summary>
    /// Gets or sets the offset of the field in bytes.
    /// </summary>
    public long Offset { get => BitOffset / 8; }

    /// <summary>
    /// Gets or sets the offset of the field in bytes.
    /// </summary>
    public long BitOffset { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();

        if (Visibility != CppVisibility.Default)
        {
            builder.Append(Visibility.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        if (StorageQualifier != CppStorageQualifier.None)
        {
            builder.Append(StorageQualifier.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        builder.Append(Type.GetDisplayName());
        builder.Append(' ');
        builder.Append(Name);

        if (InitExpression != null)
        {
            builder.Append(" = ");
            var initExpressionStr = InitExpression.ToString();
            if (string.IsNullOrEmpty(initExpressionStr))
            {
                builder.Append(InitValue);
            }
            else
            {
                builder.Append(initExpressionStr);
            }
        }
        else if (InitValue != null)
        {
            builder.Append(" = ");
            builder.Append(InitValue);
        }

        return builder.ToString();
    }
}
