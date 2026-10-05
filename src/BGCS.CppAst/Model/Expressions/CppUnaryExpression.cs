// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System.Text;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// A unary expression.
/// </summary>
public class CppUnaryExpression : CppExpression
{
    /// <summary>
    /// Creates an expression whose first child supplies its unary operand.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="kind">
    /// The native unary expression category.
    /// </param>
    public CppUnaryExpression(
        CXCursor cursor,
        CppExpressionKind kind
    ) : base(cursor, kind)
    {
    }

    /// <summary>
    /// The unary operator as a string.
    /// </summary>
    public string @operator { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(this.@operator);
        if (this.arguments != null && this.arguments.Count > 0)
        {
            builder.Append(this.arguments[0]);
        }

        return builder.ToString();
    }
}
