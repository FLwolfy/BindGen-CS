// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Utilities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Expressions;
/// <summary>
/// Base class for expressions used in <see cref="CppField.InitExpression"/> and <see cref="CppParameter.InitExpression"/>
/// </summary>
public abstract class CppExpression : CppElement
{
    protected CppExpression(CXCursor cursor, CppExpressionKind kind) : base(cursor)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the kind of this expression.
    /// </summary>
    public CppExpressionKind Kind { get; }

    /// <summary>
    /// Gets the arguments of this expression. Might be null.
    /// </summary>
    public List<CppExpression> Arguments { get; set; }

    /// <summary>
    /// Adds an argument to this expression.
    /// </summary>
    /// <param name="arg">An argument</param>
    public void AddArgument(CppExpression arg)
    {
        if (arg == null) throw new ArgumentNullException(nameof(arg));
        if (Arguments == null) Arguments = [];
        Arguments.Add(arg);
    }

    protected void ArgumentsSeparatedByCommaToString(StringBuilder builder)
    {
        if (Arguments != null)
        {
            for (var i = 0; i < Arguments.Count; i++)
            {
                var expression = Arguments[i];
                if (i > 0) builder.Append(", ");
                builder.Append(expression);
            }
        }
    }
}

/// <summary>
/// An expression that is not exposed in details but only through a list of <see cref="CppToken"/>
/// and a textual representation
/// </summary>
public class CppRawExpression : CppExpression
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppRawExpression"/>.
    /// </summary>
    public CppRawExpression(CXCursor cursor, CppExpressionKind kind) : base(cursor, kind)
    {
        Tokens = [];
    }

    /// <summary>
    /// Gets the tokens associated to this raw expression.
    /// </summary>
    public List<CppToken> Tokens { get; }

    /// <summary>
    /// Gets or sets a textual representation from the tokens.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Update the <see cref="Text"/> representation from the <see cref="Tokens"/>.
    /// </summary>
    public void UpdateTextFromTokens()
    {
        Text = CppToken.TokensToString(Tokens);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Text;
    }

    /// <summary>
    /// Executes public operation <c>AppendTokens</c>.
    /// </summary>
    public void AppendTokens(CXCursor cursor)
    {
        Tokenizer tokenizer = new(cursor);
        for (int i = 0; i < tokenizer.Count; i++)
        {
            Tokens.Add(tokenizer[i]);
        }
        UpdateTextFromTokens();
    }
}

/// <summary>
/// A C++ Init list expression `{ a, b, c }`
/// </summary>
public class CppInitListExpression : CppExpression
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppInitListExpression"/>.
    /// </summary>
    public CppInitListExpression(CXCursor cursor) : base(cursor, CppExpressionKind.InitList)
    {
    }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('{');
        ArgumentsSeparatedByCommaToString(builder);
        builder.Append('}');
        return builder.ToString();
    }
}

/// <summary>
/// A binary expression
/// </summary>
public class CppBinaryExpression : CppExpression
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppBinaryExpression"/>.
    /// </summary>
    public CppBinaryExpression(CXCursor cursor, CppExpressionKind kind) : base(cursor, kind)
    {
    }

    /// <summary>
    /// The binary operator as a string.
    /// </summary>
    public string Operator { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Arguments != null && Arguments.Count > 0)
        {
            builder.Append(Arguments[0]);
        }

        builder.Append(' ');
        builder.Append(Operator);
        builder.Append(' ');

        if (Arguments != null && Arguments.Count > 1)
        {
            builder.Append(Arguments[1]);
        }
        return builder.ToString();
    }
}

/// <summary>
/// A unary expression.
/// </summary>
public class CppUnaryExpression : CppExpression
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppUnaryExpression"/>.
    /// </summary>
    public CppUnaryExpression(CXCursor cursor, CppExpressionKind kind) : base(cursor, kind)
    {
    }

    /// <summary>
    /// The unary operator as a string.
    /// </summary>
    public string Operator { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Operator);
        if (Arguments != null && Arguments.Count > 0)
        {
            builder.Append(Arguments[0]);
        }
        return builder.ToString();
    }
}

/// <summary>
/// An expression surrounding another expression by parenthesis.
/// </summary>
public class CppParenExpression : CppExpression
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppParenExpression"/>.
    /// </summary>
    public CppParenExpression(CXCursor cursor) : base(cursor, CppExpressionKind.Paren)
    {
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append('(');
        ArgumentsSeparatedByCommaToString(builder);
        builder.Append(')');
        return builder.ToString();
    }
}

/// <summary>
/// A literal expression.
/// </summary>
public class CppLiteralExpression : CppExpression
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppLiteralExpression"/>.
    /// </summary>
    public CppLiteralExpression(CXCursor cursor, CppExpressionKind kind, string value) : base(cursor, kind)
    {
        Value = value;
    }

    /// <summary>
    /// A textual representation of the literal value.
    /// </summary>
    public string Value { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }
}
