using System.Collections.Generic;
using BGCS.Language.Lexing;
using BGCS.Language.Parsing;

namespace BGCS.Language.CSharp.Analyzers;

using BGCS.Language.CSharp.Nodes;

/// <summary>
/// Collects member modifiers and dispatches supported field or method syntax within the current class scope.
/// </summary>
public class ClassMemberAnalyzer : ISyntaxAnalyzer
{
    private static readonly KeywordType[] ModifierKeywords = [KeywordType.Public, KeywordType.Internal, KeywordType.Protected, KeywordType.Private, KeywordType.Readonly, KeywordType.Static, KeywordType.Const, KeywordType.Unsafe];
    /// <summary>
    /// Consumes supported syntax at the cursor and reports malformed recognized declarations through the context diagnostics.
    /// </summary>
    /// <param name="context">
    /// The mutable context owned by the active parse operation.
    /// </param>
    /// <returns>
    /// Success after cursor progress, Unrecognised when this analyzer does not match, or Error when recognized syntax is invalid.
    /// </returns>
    public AnalyserResult Analyze(ParserContext context)
    {
        if (context.current is not ClassNode)
        {
            return AnalyserResult.Unrecognised;
        }

        if (context.isEnd)
        {
            return AnalyserResult.Unrecognised;
        }

        int start = context.currentTokenIndex;
        List<KeywordType> modifiers = [];
        while (!context.isEnd && context.currentToken.isKeyword && IsModifier(context.currentToken.keywordType))
        {
            modifiers.Add(context.currentToken.keywordType);
            context.MoveNext();
        }

        if (context.isEnd)
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        // Let scope closing be handled by AnalyseScoped.
        if (context.currentToken.isPunctuation && context.currentToken == '}')
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        if (!IsTypeToken(context.currentToken))
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        string memberType = context.currentToken.AsString();
        context.MoveNext();
        if (context.isEnd || !context.currentToken.isIdentifier)
        {
            context.MoveTo(start);
            return AnalyserResult.Unrecognised;
        }

        string memberName = context.currentToken.AsString();
        context.MoveNext();
        if (context.isEnd)
        {
            context.diagnostics.Error("Syntax Error: Unexpected end of file.");
            return AnalyserResult.Error;
        }

        if (context.currentToken.isPunctuation && context.currentToken == '(')
        {
            return ParseMethod(context, modifiers, memberType, memberName);
        }

        return ParseField(context, modifiers, memberType, memberName);
    }

    private static AnalyserResult ParseMethod(
        ParserContext context,
        List<KeywordType> modifiers,
        string returnType,
        string name
    ) {
        List<string> parameters = [];
        context.MoveNext(); // consume '('
        while (!context.isEnd)
        {
            if (context.currentToken.isPunctuation && context.currentToken == ')')
            {
                context.MoveNext();
                break;
            }

            if (context.currentToken.isPunctuation && context.currentToken == ',')
            {
                context.MoveNext();
                continue;
            }

            if (context.currentToken.isIdentifier || context.currentToken.isKeyword)
            {
                parameters.Add(context.currentToken.AsString());
                context.MoveNext();
                continue;
            }

            context.diagnostics.Error("Syntax Error: Expected token ) or parameter", context.currentToken.location);
            return AnalyserResult.Error;
        }

        if (context.isEnd)
        {
            context.diagnostics.Error("Syntax Error: Expected token )");
            return AnalyserResult.Error;
        }

        MethodNode node = new(name, modifiers.ToArray(), parameters.ToArray(), returnType);
        return context.AnalyseScoped(node);
    }

    private static AnalyserResult ParseField(
        ParserContext context,
        List<KeywordType> modifiers,
        string type,
        string name
    ) {
        string? expression = null;
        if (context.currentToken.isOperator && context.currentToken == '=')
        {
            context.MoveNext();
            if (context.isEnd || (!context.currentToken.isIdentifier && !context.currentToken.isKeyword && !context.currentToken.isLiteral))
            {
                context.diagnostics.Error("Syntax Error: Expected expression for field", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            expression = context.currentToken.AsString();
            context.MoveNext();
        }

        if (context.isEnd || !context.currentToken.isPunctuation || context.currentToken != ';')
        {
            context.diagnostics.Error("Syntax Error: ; expected", context.isEnd ? null : context.currentToken.location);
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
        return token.isIdentifier || token.isKeyword;
    }
}
