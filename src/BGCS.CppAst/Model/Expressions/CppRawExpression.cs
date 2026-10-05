// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System.Collections.Generic;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// An expression that is not exposed in details but only through a list of <see cref = "CppToken"/>
/// and a textual representation
/// </summary>
public class CppRawExpression : CppExpression
{
    /// <summary>
    /// Creates an expression preserving raw token spelling before semantic lowering.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="kind">
    /// The native expression category.
    /// </param>
    public CppRawExpression(
        CXCursor cursor,
        CppExpressionKind kind
    ) : base(cursor, kind)
    {
        this.tokens = [];
        this.text = string.Empty;
    }

    /// <summary>
    /// Gets the tokens associated to this raw expression.
    /// </summary>
    public List<CppToken> tokens { get; }
    /// <summary>
    /// Gets or sets a textual representation from the tokens.
    /// </summary>
    public string text { get; set; }

    /// <summary>
    /// Update the <see cref = "text"/> representation from the <see cref = "tokens"/>.
    /// </summary>
    public void UpdateTextFromTokens()
    {
        this.text = CppToken.TokensToString(this.tokens);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.text;
    }

    /// <summary>
    /// Appends borrowed cursor tokens and then recomputes the complete raw expression spelling.
    /// </summary>
    /// <param name="cursor">
    /// The native cursor whose token extent is read while its compilation remains alive.
    /// </param>
    public void AppendTokens(CXCursor cursor)
    {
        Tokenizer tokenizer = new(cursor);
        for (int i = 0; i < tokenizer.count; i++)
        {
            this.tokens.Add(tokenizer[i]);
        }

        UpdateTextFromTokens();
    }
}
