namespace BGCS.Language.CSharp.Analyzers;

using BGCS.Language.CSharp.Nodes;

public class ClassMemberAnalyzer : ISyntaxAnalyzer
{
    private static readonly KeywordType[] ModifierKeywords =
    [
        KeywordType.Public,
        KeywordType.Internal,
        KeywordType.Protected,
        KeywordType.Private,
        KeywordType.Readonly,
        KeywordType.Static,
        KeywordType.Const,
        KeywordType.Unsafe
    ];

    public AnalyserResult Analyze(ParserContext context)
    {
        if (context.Current is not ClassNode)
        {
            return AnalyserResult.Unrecognised;
        }

        if (context.IsEnd)
        {
            return AnalyserResult.Unrecognised;
        }

        int start = context.CurrentTokenIndex;
        List<KeywordType> modifiers = [];

        while (!context.IsEnd && context.CurrentToken.IsKeyword && IsModifier(context.CurrentToken.KeywordType))
        {
            modifiers.Add(context.CurrentToken.KeywordType);
            context.MoveNext();
        }

        if (context.IsEnd)
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        // Let scope closing be handled by AnalyseScoped.
        if (context.CurrentToken.IsPunctuation && context.CurrentToken == '}')
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        if (!IsTypeToken(context.CurrentToken))
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        string memberType = context.CurrentToken.AsString();
        context.MoveNext();

        if (context.IsEnd || !context.CurrentToken.IsIdentifier)
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        string memberName = context.CurrentToken.AsString();
        context.MoveNext();

        if (context.IsEnd)
        {
            context.Diagnostics.Error("Syntax Error: Unexpected end of file.");
            return AnalyserResult.Error;
        }

        if (context.CurrentToken.IsPunctuation && context.CurrentToken == '(')
        {
            return ParseMethod(context, modifiers, memberType, memberName);
        }

        return ParseField(context, modifiers, memberType, memberName);
    }

    private static AnalyserResult ParseMethod(ParserContext context, List<KeywordType> modifiers, string returnType, string name)
    {
        List<string> parameters = [];
        context.MoveNext(); // consume '('

        while (!context.IsEnd)
        {
            if (context.CurrentToken.IsPunctuation && context.CurrentToken == ')')
            {
                context.MoveNext();
                break;
            }

            if (context.CurrentToken.IsPunctuation && context.CurrentToken == ',')
            {
                context.MoveNext();
                continue;
            }

            if (context.CurrentToken.IsIdentifier || context.CurrentToken.IsKeyword)
            {
                parameters.Add(context.CurrentToken.AsString());
                context.MoveNext();
                continue;
            }

            context.Diagnostics.Error("Syntax Error: Expected token ) or parameter", context.CurrentToken.Location);
            return AnalyserResult.Error;
        }

        if (context.IsEnd)
        {
            context.Diagnostics.Error("Syntax Error: Expected token )");
            return AnalyserResult.Error;
        }

        MethodNode node = new(name, modifiers.ToArray(), parameters.ToArray(), returnType);
        return context.AnalyseScoped(node);
    }

    private static AnalyserResult ParseField(ParserContext context, List<KeywordType> modifiers, string type, string name)
    {
        string? expression = null;

        if (context.CurrentToken.IsOperator && context.CurrentToken == '=')
        {
            context.MoveNext();
            if (context.IsEnd || (!context.CurrentToken.IsIdentifier && !context.CurrentToken.IsKeyword && !context.CurrentToken.IsLiteral))
            {
                context.Diagnostics.Error("Syntax Error: Expected expression for field", context.IsEnd ? null : context.CurrentToken.Location);
                return AnalyserResult.Error;
            }

            expression = context.CurrentToken.AsString();
            context.MoveNext();
        }

        if (context.IsEnd || !context.CurrentToken.IsPunctuation || context.CurrentToken != ';')
        {
            context.Diagnostics.Error("Syntax Error: ; expected", context.IsEnd ? null : context.CurrentToken.Location);
            return AnalyserResult.Error;
        }

        context.MoveNext();

        FieldNode node = new(type, name, modifiers.ToArray(), expression);
        context.AppendNode(node);
        return AnalyserResult.Success;
    }

    private static bool IsModifier(KeywordType keywordType)
    {
        for (int i = 0; i < ModifierKeywords.Length; i++)
        {
            if (ModifierKeywords[i] == keywordType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeToken(Token token)
    {
        return token.IsIdentifier || token.IsKeyword;
    }
}
