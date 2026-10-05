// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ field (of a struct/class) or global variable.
/// </summary>
public sealed class CppField : CppDeclaration, ICppMemberWithVisibility, ICppAttributeContainer
{
    /// <summary>
    /// Creates a mutable native field projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="type">
    /// The non-null borrowed native field type.
    /// </param>
    /// <param name="name">
    /// The native field identifier, empty for anonymous storage.
    /// </param>
    public CppField(
        CXCursor cursor,
        CppType type,
        string name
    ) : base(cursor)
    {
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        this.name = name;
        this.attributes = [];
    }

    /// <inheritdoc/>
    public CppVisibility visibility { get; set; }
    /// <summary>
    /// Gets or sets the storage qualifier of this field/variable.
    /// </summary>
    public CppStorageQualifier storageQualifier { get; set; }
    /// <summary>
    /// Gets attached attributes. Might be null.
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
    /// <summary>
    /// Gets the type of this field/variable.
    /// </summary>
    public CppType type { get; set; }
    /// <inheritdoc/>
    public string name { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating if this field was created from an anonymous type
    /// </summary>
    public bool isAnonymous { get; set; }
    /// <summary>
    /// Gets the associated init value (either an integer or a string...)
    /// </summary>
    public CppValue? initValue { get; set; }
    /// <summary>
    /// Gets the associated init value as an expression.
    /// </summary>
    public CppExpression? initExpression { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating that this field is a bit field. See <see cref = "bitFieldWidth"/> to get the width of this field if <see cref = "isBitField"/> is <c>true</c>
    /// </summary>
    public bool isBitField { get; set; }
    /// <summary>
    /// Gets or sets the number of bits for this bit field. Only valid if <see cref = "isBitField"/> is <c>true</c>.
    /// </summary>
    public int bitFieldWidth { get; set; }
    /// <summary>
    /// Gets or sets the offset of the field in bytes.
    /// </summary>
    public long offset { get => this.bitOffset / 8; }
    /// <summary>
    /// Gets or sets the offset of the field in bytes.
    /// </summary>
    public long bitOffset { get; set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (this.visibility != CppVisibility.Default)
        {
            builder.Append(this.visibility.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        if (this.storageQualifier != CppStorageQualifier.None)
        {
            builder.Append(this.storageQualifier.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        builder.Append(this.type.GetDisplayName());
        builder.Append(' ');
        builder.Append(this.name);
        if (this.initExpression != null)
        {
            builder.Append(" = ");
            var initExpressionStr = this.initExpression.ToString();
            if (string.IsNullOrEmpty(initExpressionStr))
            {
                builder.Append(this.initValue);
            }
            else
            {
                builder.Append(initExpressionStr);
            }
        }
        else if (this.initValue != null)
        {
            builder.Append(" = ");
            builder.Append(this.initValue);
        }

        return builder.ToString();
    }
}
