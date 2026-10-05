// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System.Text;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// A binary expression
/// </summary>
public class CppBinaryExpression : CppExpression
{
    /// <summary>
    /// Creates an expression whose first two child expressions supply the binary operands.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="kind">
    /// The native binary expression category.
    /// </param>
    public CppBinaryExpression(
        CXCursor cursor,
        CppExpressionKind kind
    ) : base(cursor, kind)
    {
    }

    /// <summary>
    /// The binary operator as a string.
    /// </summary>
    public string @operator { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (this.arguments != null && this.arguments.Count > 0)
        {
            builder.Append(this.arguments[0]);
        }

        builder.Append(' ');
        builder.Append(this.@operator);
        builder.Append(' ');
        if (this.arguments != null && this.arguments.Count > 1)
        {
            builder.Append(this.arguments[1]);
        }

        return builder.ToString();
    }
}
