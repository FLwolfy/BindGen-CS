using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Extensions;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// A C++ token used by <see cref = "CppMacro"/>.
/// </summary>
public class CppToken : CppElement
{
    /// <summary>
    /// Captures a native token category and spelling for attempt-local expression analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="kind">
    /// The native token category.
    /// </param>
    /// <param name="text">
    /// The token spelling retained by this projection.
    /// </param>
    public CppToken(
        CXCursor cursor,
        CppTokenKind kind,
        string text
    ) : base(cursor)
    {
        this.kind = kind;
        this.text = text;
    }

    /// <summary>
    /// Gets or sets the kind of this token.
    /// </summary>
    public CppTokenKind kind { get; set; }
    /// <summary>
    /// Gets or sets the text of this token.
    /// </summary>
    public string text { get; set; }

    /// <inheritdoc/>
    public override string ToString() => this.text;
    /// <summary>
    /// Concatenates native token spellings while omitting comments and separating adjacent identifier or keyword tokens.
    /// </summary>
    /// <param name="tokens">
    /// The ordered borrowed token sequence to render.
    /// </param>
    /// <returns>
    /// The rendered token spelling, including an empty string for an empty or comment-only sequence.
    /// </returns>
    public static string TokensToString(IEnumerable<CppToken> tokens)
    {
        var builder = new StringBuilder();
        CppTokenKind previousKind = 0;
        foreach (var token in tokens)
        {
            if (token.kind == CppTokenKind.Comment)
                continue;
            // If previous token and new token are identifiers/keyword, we need a space between them
            if (previousKind.IsIdentifierOrKeyword() && token.kind.IsIdentifierOrKeyword())
            {
                builder.Append(' ');
            }

            builder.Append(token.text);
            previousKind = token.kind;
        }

        return builder.ToString();
    }
}
