using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Expressions;
/// <summary>
/// A C++ token used by <see cref="CppMacro"/>.
/// </summary>
public class CppToken : CppElement
{
    /// <summary>
    /// Creates a new instance of a C++ token.
    /// </summary>
    /// <param name="kind">Kind of this token</param>
    /// <param name="text">Text of this token</param>
    public CppToken(CXCursor cursor, CppTokenKind kind, string text) : base(cursor)
    {
        Kind = kind;
        Text = text;
    }

    /// <summary>
    /// Gets or sets the kind of this token.
    /// </summary>
    public CppTokenKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the text of this token.
    /// </summary>
    public string Text { get; set; }

    /// <inheritdoc />
    public override string ToString() => Text;

    /// <summary>
    /// Executes public operation <c>TokensToString</c>.
    /// </summary>
    public static string TokensToString(IEnumerable<CppToken> tokens)
    {
        var builder = new StringBuilder();
        CppTokenKind previousKind = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == CppTokenKind.Comment) continue;

            // If previous token and new token are identifiers/keyword, we need a space between them
            if (previousKind.IsIdentifierOrKeyword() && token.Kind.IsIdentifierOrKeyword())
            {
                builder.Append(' ');
            }
            builder.Append(token.Text);
            previousKind = token.Kind;
        }

        return builder.ToString();
    }
}
