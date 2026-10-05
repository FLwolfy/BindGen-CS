// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// A literal expression.
/// </summary>
public class CppLiteralExpression : CppExpression
{
    /// <summary>
    /// Captures the native spelling of a literal expression.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="kind">
    /// The native literal category.
    /// </param>
    /// <param name="value">
    /// The literal spelling retained without decoding or normalization.
    /// </param>
    public CppLiteralExpression(
        CXCursor cursor,
        CppExpressionKind kind,
        string value
    ) : base(cursor, kind)
    {
        this.value = value;
    }

    /// <summary>
    /// A textual representation of the literal value.
    /// </summary>
    public string value { get; set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.value;
    }
}
