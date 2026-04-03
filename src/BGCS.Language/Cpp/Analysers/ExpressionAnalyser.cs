namespace BGCS.Language.Cpp.Analysers;

public class ExpressionNode : SyntaxNode
{
    public override string ToString()
    {
        return "expr";
    }
}

public class FunctionCallNode : SyntaxNode
{
    public FunctionCallNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString()
    {
        return $"call: {Name}";
    }
}

public class CastNode : SyntaxNode
{
    public CastNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString()
    {
        return $"cast: {Name}";
    }
}

public class OperatorNode : SyntaxNode
{
    public OperatorNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString()
    {
        return $"op: {Name}";
    }
}

public class GroupNode : SyntaxNode
{
    public override string ToString()
    {
        return "group";
    }
}

public class TypeNode : SyntaxNode
{
    public TypeNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString()
    {
        return $"type: {Name}";
    }
}

public class VariableNode : SyntaxNode
{
    public VariableNode(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString()
    {
        return $"var: {Name}";
    }
}

public class ValueNode : SyntaxNode
{
    public ValueNode(string value, LiteralType type, NumberType numberType)
    {
        Value = value;
        Type = type;
        NumberType = numberType;
    }

    public string Value { get; }

    public LiteralType Type { get; }

    public NumberType NumberType { get; }

    public override string ToString()
    {
        return $"value: {Value}";
    }
}

public class ExpressionAnalyser : ISyntaxAnalyzer
{
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
