// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System.Text;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// An expression surrounding another expression by parenthesis.
/// </summary>
public class CppParenExpression : CppExpression
{
    /// <summary>
    /// Creates a parenthesized expression retaining its ordered child arguments.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    public CppParenExpression(CXCursor cursor) : base(cursor, CppExpressionKind.Paren)
    {
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('(');
        ArgumentsSeparatedByCommaToString(builder);
        builder.Append(')');
        return builder.ToString();
    }
}
