// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Model.Declarations;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Expressions;

/// <summary>
/// Base class for expressions used in <see cref = "CppField.initExpression"/> and <see cref = "CppParameter.initExpression"/>
/// </summary>
public abstract class CppExpression : CppElement
{
    /// <summary>
    /// Creates an analysis model borrowing native data from its owning compilation.
    /// </summary>
    /// <param name="cursor">Borrowed expression cursor from a live compilation.</param>
    /// <param name="kind">Semantic expression category represented by this node.</param>
    protected CppExpression(
        CXCursor cursor,
        CppExpressionKind kind
    ) : base(cursor)
    {
        this.kind = kind;
        this.arguments = [];
    }

    /// <summary>
    /// Gets the kind of this expression.
    /// </summary>
    public CppExpressionKind kind { get; }
    /// <summary>
    /// Gets the arguments of this expression. Might be null.
    /// </summary>
    public List<CppExpression> arguments { get; set; }

    /// <summary>
    /// Adds an argument to this expression.
    /// </summary>
    /// <param name = "arg">An argument</param>
    public void AddArgument(CppExpression arg)
    {
        if (arg == null)
            throw new ArgumentNullException(nameof(arg));
        if (this.arguments == null)
            this.arguments = [];
        this.arguments.Add(arg);
    }

    /// <summary>
    /// Appends expression arguments in source order, separated by commas.
    /// </summary>
    /// <param name="builder">Caller-owned destination builder; existing content is preserved.</param>
    protected void ArgumentsSeparatedByCommaToString(StringBuilder builder)
    {
        if (this.arguments != null)
        {
            for (var i = 0; i < this.arguments.Count; i++)
            {
                var expression = this.arguments[i];
                if (i > 0)
                    builder.Append(", ");
                builder.Append(expression);
            }
        }
    }
}
