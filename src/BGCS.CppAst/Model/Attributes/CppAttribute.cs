using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using System.Text;

namespace BGCS.CppAst.Model.Attributes;
/// <summary>
/// An attached C++ attribute
/// </summary>
public class CppAttribute : CppElement
{
    public CppAttribute(CXCursor cursor, string name, AttributeKind kind) : base(cursor)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
    }

    public CppAttribute(CXComment comment, string name, AttributeKind kind) : base(CXCursor.Null)
    {
        Comment = comment;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
    }

    public CXComment Comment { get; set; }

    /// <summary>
    /// Gets or sets the scope of this attribute
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Gets the attribute name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets the attribute arguments
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Gets a boolean indicating whether this attribute is variadic
    /// </summary>
    public bool IsVariadic { get; set; }

    public AttributeKind Kind { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        StringBuilder builder = new();

        ////builder.Append("[[");

        builder.Append(Name);
        if (Arguments != null)
        {
            builder.Append('(').Append(Arguments).Append(')');
        }

        if (IsVariadic)
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
