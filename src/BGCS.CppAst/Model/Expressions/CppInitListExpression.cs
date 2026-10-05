// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System.Text;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// A C++ Init list expression `{ a, b, c }`
/// </summary>
public class CppInitListExpression : CppExpression
{
    /// <summary>
    /// Creates an initializer-list expression whose children retain native argument order.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    public CppInitListExpression(CXCursor cursor) : base(cursor, CppExpressionKind.InitList)
    {
    }

    /// <summary>
    /// Formats ordered argument expressions inside native initializer-list braces.
    /// </summary>
    /// <returns>
    /// The current diagnostic initializer spelling, including braces for an empty list.
    /// </returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('{');
        ArgumentsSeparatedByCommaToString(builder);
        builder.Append('}');
        return builder.ToString();
    }
}
