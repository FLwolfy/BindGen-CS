using BGCS.Language.Cpp.Nodes;
using BGCS.Language.Diagnostics;
using BGCS.Language.Lexing;
using BGCS.Language.Parsing;
using BGCS.Language.Syntax;
namespace BGCS.Language.Cpp.Analysers;

/// <summary>
/// Parses supported macro literals, identifiers, calls, casts, grouping, and unary or binary operators into expression nodes.
/// </summary>
public class ExpressionAnalyser : ISyntaxAnalyzer
{
    /// <summary>
    /// Consumes one supported expression at the current cursor and appends its root only after parsing succeeds.
    /// </summary>
    /// <param name="context">
    /// The mutable context owned by this parse operation.
    /// </param>
    /// <returns>
    /// Success after consuming and appending an expression, Unrecognised for another declaration, or Error with diagnostics for malformed syntax.
    /// </returns>
    public AnalyserResult Analyze(ParserContext context)
    {
        if (context.isEnd || !IsExpressionStart(context.currentToken))
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

    private static SyntaxNode? ParseExpression(
        ParserContext context,
        int minPrecedence,
        bool stopAtComma,
        bool stopAtRightParen
    ) {
        SyntaxNode? left = ParseUnary(context, stopAtComma, stopAtRightParen);
        if (left == null)
        {
            return null;
        }

        while (!context.isEnd)
        {
            if (stopAtComma && context.currentToken.isPunctuation && context.currentToken == ',')
            {
                break;
            }

            if (stopAtRightParen && context.currentToken.isPunctuation && context.currentToken == ')')
            {
                break;
            }

            if (!context.currentToken.isOperator)
            {
                break;
            }

            string op = context.currentToken.AsString();
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

    private static SyntaxNode? ParseUnary(
        ParserContext context,
        bool stopAtComma,
        bool stopAtRightParen
    ) {
        if (context.isEnd)
        {
            context.diagnostics.Error("Syntax Error: expression expected");
            return null;
        }

        if (context.currentToken.isOperator && IsUnaryOperator(context.currentToken.AsString()))
        {
            string op = context.currentToken.AsString();
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

    private static SyntaxNode? ParsePrimary(
        ParserContext context,
        bool stopAtComma,
        bool stopAtRightParen
    ) {
        if (context.isEnd)
        {
            context.diagnostics.Error("Syntax Error: expression expected");
            return null;
        }

        if (context.currentToken.isLiteral)
        {
            ValueNode literal = new(context.currentToken.AsString(), context.currentToken.literalType, context.currentToken.numberType);
            context.MoveNext();
            return literal;
        }

        if (context.currentToken.isIdentifier)
        {
            string identifier = context.currentToken.AsString();
            context.MoveNext();
            if (!context.isEnd && context.currentToken.isPunctuation && context.currentToken == '(')
            {
                return ParseFunctionCall(context, identifier);
            }

            return new VariableNode(identifier);
        }

        if (context.currentToken.isPunctuation && context.currentToken == '(')
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

            if (context.isEnd || !context.currentToken.isPunctuation || context.currentToken != ')')
            {
                context.diagnostics.Error("Syntax Error: ) expected", GetSafeLocation(context));
                return null;
            }

            context.MoveNext();
            GroupNode group = new();
            group.AddChild(inner);
            return group;
        }

        context.diagnostics.Error("Syntax Error: expression expected", GetSafeLocation(context));
        return null;
    }

    private static SyntaxNode? ParseFunctionCall(
        ParserContext context,
        string name
    ) {
        FunctionCallNode call = new(name);
        context.MoveNext(); // consume '('
        if (!context.isEnd && context.currentToken.isPunctuation && context.currentToken == ')')
        {
            context.MoveNext();
            return call;
        }

        while (!context.isEnd)
        {
            SyntaxNode? argument = ParseExpression(context, minPrecedence: 1, stopAtComma: true, stopAtRightParen: true);
            if (argument == null)
            {
                return null;
            }

            call.AddChild(argument);
            if (context.isEnd)
            {
                context.diagnostics.Error("Syntax Error: ) expected");
                return null;
            }

            if (context.currentToken.isPunctuation && context.currentToken == ',')
            {
                context.MoveNext();
                continue;
            }

            if (context.currentToken.isPunctuation && context.currentToken == ')')
            {
                context.MoveNext();
                return call;
            }

            context.diagnostics.Error("Syntax Error: , or ) expected", context.currentToken.location);
            return null;
        }

        context.diagnostics.Error("Syntax Error: ) expected");
        return null;
    }

    private static SyntaxNode? ParseCast(
        ParserContext context,
        bool stopAtComma,
        bool stopAtRightParen
    ) {
        context.MoveNext(); // '('
        if (context.isEnd || !context.currentToken.isIdentifier)
        {
            context.diagnostics.Error("Syntax Error: type expected", GetSafeLocation(context));
            return null;
        }

        string typeName = context.currentToken.AsString();
        context.MoveNext();
        if (context.isEnd || !context.currentToken.isPunctuation || context.currentToken != ')')
        {
            context.diagnostics.Error("Syntax Error: ) expected", GetSafeLocation(context));
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
        if (!t1.isIdentifier || !t2.isPunctuation || t2 != ')')
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
        return context.isEnd ? null : context.currentToken.location;
    }

    private static bool IsExpressionStart(Token token)
    {
        return token.isIdentifier || token.isLiteral || (token.isPunctuation && token == '(') || (token.isOperator && IsUnaryOperator(token.AsString()));
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
