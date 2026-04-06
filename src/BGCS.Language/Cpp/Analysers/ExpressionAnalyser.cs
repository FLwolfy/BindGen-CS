namespace BGCS.Language.Cpp.Analysers;

/// <summary>
/// Defines the public class <c>ExpressionNode</c>.
/// </summary>
public class ExpressionNode : SyntaxNode
{
    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return "expr";
    }
}

/// <summary>
/// Defines the public class <c>FunctionCallNode</c>.
/// </summary>
public class FunctionCallNode : SyntaxNode
{
    /// <summary>
    /// Initializes a new instance of <see cref="FunctionCallNode"/>.
    /// </summary>
    public FunctionCallNode(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets <c>Name</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return $"call: {Name}";
    }
}

/// <summary>
/// Defines the public class <c>CastNode</c>.
/// </summary>
public class CastNode : SyntaxNode
{
    /// <summary>
    /// Initializes a new instance of <see cref="CastNode"/>.
    /// </summary>
    public CastNode(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets <c>Name</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return $"cast: {Name}";
    }
}

/// <summary>
/// Defines the public class <c>OperatorNode</c>.
/// </summary>
public class OperatorNode : SyntaxNode
{
    /// <summary>
    /// Initializes a new instance of <see cref="OperatorNode"/>.
    /// </summary>
    public OperatorNode(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets <c>Name</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return $"op: {Name}";
    }
}

/// <summary>
/// Defines the public class <c>GroupNode</c>.
/// </summary>
public class GroupNode : SyntaxNode
{
    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return "group";
    }
}

/// <summary>
/// Defines the public class <c>TypeNode</c>.
/// </summary>
public class TypeNode : SyntaxNode
{
    /// <summary>
    /// Initializes a new instance of <see cref="TypeNode"/>.
    /// </summary>
    public TypeNode(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets <c>Name</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return $"type: {Name}";
    }
}

/// <summary>
/// Defines the public class <c>VariableNode</c>.
/// </summary>
public class VariableNode : SyntaxNode
{
    /// <summary>
    /// Initializes a new instance of <see cref="VariableNode"/>.
    /// </summary>
    public VariableNode(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets <c>Name</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return $"var: {Name}";
    }
}

/// <summary>
/// Defines the public class <c>ValueNode</c>.
/// </summary>
public class ValueNode : SyntaxNode
{
    /// <summary>
    /// Initializes a new instance of <see cref="ValueNode"/>.
    /// </summary>
    public ValueNode(string value, LiteralType type, NumberType numberType)
    {
        Value = value;
        Type = type;
        NumberType = numberType;
    }

    /// <summary>
    /// Gets <c>Value</c>.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets <c>Type</c>.
    /// </summary>
    public LiteralType Type { get; }

    /// <summary>
    /// Gets <c>NumberType</c>.
    /// </summary>
    public NumberType NumberType { get; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        return $"value: {Value}";
    }
}

/// <summary>
/// Defines the public class <c>ExpressionAnalyser</c>.
/// </summary>
public class ExpressionAnalyser : ISyntaxAnalyzer
{
    /// <summary>
    /// Executes public operation <c>Analyze</c>.
    /// </summary>
    public AnalyserResult Analyze(ParserContext context)
    {
        if (context.IsEnd || !IsExpressionStart(context.CurrentToken))
        {
            return AnalyserResult.Unrecognised;
        }

        ExpressionNode expressionRoot = new();
        SyntaxNode? expression = ParseExpression(context, minPrecedence: 1, stopAtComma: false, stopAtRightParen: false);
        if (expression == null)
        {
            return AnalyserResult.Error;
        }

        expressionRoot.AddChild(expression);
        context.AppendNode(expressionRoot);
        return AnalyserResult.Success;
    }

    private static SyntaxNode? ParseExpression(ParserContext context, int minPrecedence, bool stopAtComma, bool stopAtRightParen)
    {
        SyntaxNode? left = ParseUnary(context, stopAtComma, stopAtRightParen);
        if (left == null)
        {
            return null;
        }

        while (!context.IsEnd)
        {
            if (stopAtComma && context.CurrentToken.IsPunctuation && context.CurrentToken == ',')
            {
                break;
            }

            if (stopAtRightParen && context.CurrentToken.IsPunctuation && context.CurrentToken == ')')
            {
                break;
            }

            if (!context.CurrentToken.IsOperator)
            {
                break;
            }

            string op = context.CurrentToken.AsString();
            int precedence = GetBinaryPrecedence(op);
            if (precedence < minPrecedence)
            {
                break;
            }

            context.MoveNext();

            int nextMinPrecedence = IsRightAssociative(op) ? precedence : precedence + 1;
            SyntaxNode? right = ParseExpression(context, nextMinPrecedence, stopAtComma, stopAtRightParen);
            if (right == null)
            {
                return null;
            }

            OperatorNode node = new(op);
            node.AddChild(left);
            node.AddChild(right);
            left = node;
        }

        return left;
    }

    private static SyntaxNode? ParseUnary(ParserContext context, bool stopAtComma, bool stopAtRightParen)
    {
        if (context.IsEnd)
        {
            context.Diagnostics.Error("Syntax Error: expression expected");
            return null;
        }

        if (context.CurrentToken.IsOperator && IsUnaryOperator(context.CurrentToken.AsString()))
        {
            string op = context.CurrentToken.AsString();
            context.MoveNext();

            SyntaxNode? operand = ParseUnary(context, stopAtComma, stopAtRightParen);
            if (operand == null)
            {
                return null;
            }

            OperatorNode node = new(op);
            node.AddChild(operand);
            return node;
        }

        return ParsePrimary(context, stopAtComma, stopAtRightParen);
    }

    private static SyntaxNode? ParsePrimary(ParserContext context, bool stopAtComma, bool stopAtRightParen)
    {
        if (context.IsEnd)
        {
            context.Diagnostics.Error("Syntax Error: expression expected");
            return null;
        }

        if (context.CurrentToken.IsLiteral)
        {
            ValueNode literal = new(context.CurrentToken.AsString(), context.CurrentToken.LiteralType, context.CurrentToken.NumberType);
            context.MoveNext();
            return literal;
        }

        if (context.CurrentToken.IsIdentifier)
        {
            string identifier = context.CurrentToken.AsString();
            context.MoveNext();

            if (!context.IsEnd && context.CurrentToken.IsPunctuation && context.CurrentToken == '(')
            {
                return ParseFunctionCall(context, identifier);
            }

            return new VariableNode(identifier);
        }

        if (context.CurrentToken.IsPunctuation && context.CurrentToken == '(')
        {
            if (IsCastStart(context))
            {
                return ParseCast(context, stopAtComma, stopAtRightParen);
            }

            context.MoveNext();
            SyntaxNode? inner = ParseExpression(context, minPrecedence: 1, stopAtComma: false, stopAtRightParen: true);
            if (inner == null)
            {
                return null;
            }

            if (context.IsEnd || !context.CurrentToken.IsPunctuation || context.CurrentToken != ')')
            {
                context.Diagnostics.Error("Syntax Error: ) expected", GetSafeLocation(context));
                return null;
            }

            context.MoveNext();
            GroupNode group = new();
            group.AddChild(inner);
            return group;
        }

        context.Diagnostics.Error("Syntax Error: expression expected", GetSafeLocation(context));
        return null;
    }

    private static SyntaxNode? ParseFunctionCall(ParserContext context, string name)
    {
        FunctionCallNode call = new(name);
        context.MoveNext(); // consume '('

        if (!context.IsEnd && context.CurrentToken.IsPunctuation && context.CurrentToken == ')')
        {
            context.MoveNext();
            return call;
        }

        while (!context.IsEnd)
        {
            SyntaxNode? argument = ParseExpression(context, minPrecedence: 1, stopAtComma: true, stopAtRightParen: true);
            if (argument == null)
            {
                return null;
            }

            call.AddChild(argument);

            if (context.IsEnd)
            {
                context.Diagnostics.Error("Syntax Error: ) expected");
                return null;
            }

            if (context.CurrentToken.IsPunctuation && context.CurrentToken == ',')
            {
                context.MoveNext();
                continue;
            }

            if (context.CurrentToken.IsPunctuation && context.CurrentToken == ')')
            {
                context.MoveNext();
                return call;
            }

            context.Diagnostics.Error("Syntax Error: , or ) expected", context.CurrentToken.Location);
            return null;
        }

        context.Diagnostics.Error("Syntax Error: ) expected");
        return null;
    }

    private static SyntaxNode? ParseCast(ParserContext context, bool stopAtComma, bool stopAtRightParen)
    {
        context.MoveNext(); // '('
        if (context.IsEnd || !context.CurrentToken.IsIdentifier)
        {
            context.Diagnostics.Error("Syntax Error: type expected", GetSafeLocation(context));
            return null;
        }

        string typeName = context.CurrentToken.AsString();
        context.MoveNext();

        if (context.IsEnd || !context.CurrentToken.IsPunctuation || context.CurrentToken != ')')
        {
            context.Diagnostics.Error("Syntax Error: ) expected", GetSafeLocation(context));
            return null;
        }

        context.MoveNext();

        SyntaxNode? operand = ParseUnary(context, stopAtComma, stopAtRightParen);
        if (operand == null)
        {
            return null;
        }

        CastNode cast = new(typeName);
        cast.AddChild(operand);
        return cast;
    }

    private static bool IsCastStart(ParserContext context)
    {
        if (!context.SeekInBounds(2))
        {
            return false;
        }

        Token t1 = context.Seek(1);
        Token t2 = context.Seek(2);

        if (!t1.IsIdentifier || !t2.IsPunctuation || t2 != ')')
        {
            return false;
        }

        if (!context.SeekInBounds(3))
        {
            return false;
        }

        Token t3 = context.Seek(3);
        return IsExpressionStart(t3);
    }

    private static SourceLocation? GetSafeLocation(ParserContext context)
    {
        return context.IsEnd ? null : context.CurrentToken.Location;
    }

    private static bool IsExpressionStart(Token token)
    {
        return token.IsIdentifier ||
               token.IsLiteral ||
               (token.IsPunctuation && token == '(') ||
               (token.IsOperator && IsUnaryOperator(token.AsString()));
    }

    private static bool IsUnaryOperator(string op)
    {
        return op is "!" or "~" or "+" or "-" or "++" or "--";
    }

    private static bool IsRightAssociative(string op)
    {
        return op == "=";
    }

    private static int GetBinaryPrecedence(string op)
    {
        return op switch
        {
            "=" => 1,
            "||" => 2,
            "&&" => 3,
            "|" => 4,
            "^" => 5,
            "&" => 6,
            "<<" or ">>" => 7,
            "+" or "-" => 8,
            "*" or "/" or "%" => 9,
            _ => -1
        };
    }
}
