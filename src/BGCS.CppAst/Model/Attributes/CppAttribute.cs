using System;
using System.Text;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Attributes;

/// <summary>
/// An attached C++ attribute
/// </summary>
public class CppAttribute : CppElement
{
    /// <summary>
    /// Creates an attribute node borrowing the native declaration cursor.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The non-null attribute identifier.
    /// </param>
    /// <param name="kind">
    /// The native attribute category.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The attribute name is null.
    /// </exception>
    public CppAttribute(
        CXCursor cursor,
        string name,
        AttributeKind kind
    ) : base(cursor)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.kind = kind;
    }

    /// <summary>
    /// Creates an attribute projection borrowing a native documentation comment.
    /// </summary>
    /// <param name="comment">
    /// The borrowed Clang comment, valid only while its owning compilation remains alive.
    /// </param>
    /// <param name="name">
    /// The non-null attribute identifier.
    /// </param>
    /// <param name="kind">
    /// The native attribute category.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The attribute name is null.
    /// </exception>
    public CppAttribute(
        CXComment comment,
        string name,
        AttributeKind kind
    ) : base(CXCursor.Null)
    {
        this.comment = comment;
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.kind = kind;
    }

    /// <summary>
    /// Gets or sets the borrowed native documentation comment; its owning compilation must remain alive.
    /// </summary>
    public CXComment comment { get; set; }
    /// <summary>
    /// Gets or sets the scope of this attribute
    /// </summary>
    public string? scope { get; set; }
    /// <summary>
    /// Gets the attribute name.
    /// </summary>
    public string name { get; set; }
    /// <summary>
    /// Gets the attribute arguments
    /// </summary>
    public string? arguments { get; set; }
    /// <summary>
    /// Gets a boolean indicating whether this attribute is variadic
    /// </summary>
    public bool isVariadic { get; set; }
    /// <summary>
    /// Gets the native attribute category captured when this projection was created.
    /// </summary>
    public AttributeKind kind { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder builder = new();
        ////builder.Append("[[");
        builder.Append(this.name);
        if (this.arguments != null)
        {
            builder.Append('(').Append(this.arguments).Append(')');
        }

        if (this.isVariadic)
        {
            builder.Append("...");
        }

        ////builder.Append("]]");
        ////if (Scope != null)
        ////{
        ////    builder.Append(" { scope:");
        ////    builder.Append(Scope).Append("::");
        ////    builder.Append("}");
        ////}
        return builder.ToString();
    }
}
