using System.Linq;
using BGCS.Language.Lexing;
using BGCS.Language.Parsing;

namespace BGCS.Language.CSharp.Analyzers
{
    using System.Collections.Generic;
    using BGCS.Language.CSharp.Nodes;

    /// <summary>
    /// Recognizes supported method signatures and retains their parameter spellings without compiling a method body.
    /// </summary>
    public class MethodAnalyser : IMemberSyntaxAnalyzer
    {
        private readonly List<Token> m_parameters = new();
        /// <summary>
        /// Consumes supported syntax at the cursor and reports malformed recognized declarations through the context diagnostics.
        /// </summary>
        /// <param name="context">
        /// The mutable context owned by the active parse operation.
        /// </param>
        /// <param name="modifiers">
        /// The declaration modifiers collected by the containing member analyzer.
        /// </param>
        /// <returns>
        /// Success after cursor progress, Unrecognised when this analyzer does not match, or Error when recognized syntax is invalid.
        /// </returns>
        public AnalyserResult Analyze(
            ParserContext context,
            IReadOnlyList<KeywordType> modifiers
        ) {
            if (!context.SeekInBounds(2))
            {
                return AnalyserResult.Unrecognised;
            }

            while (!context.isEnd && context.currentToken.isKeyword && (context.currentToken == KeywordType.Public || context.currentToken == KeywordType.Internal || context.currentToken == KeywordType.Protected || context.currentToken == KeywordType.Private || context.currentToken == KeywordType.Readonly || context.currentToken == KeywordType.Static || context.currentToken == KeywordType.Const || context.currentToken == KeywordType.Unsafe))
            {
                context.MoveNext();
            }

            if (context.isEnd || (!context.currentToken.isIdentifier && !context.currentToken.isKeyword))
            {
                context.diagnostics.Error("Syntax Error: Expected method return type", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            string returnType = context.currentToken.AsString();
            context.MoveNext();
            if (context.isEnd || !context.currentToken.isIdentifier)
            {
                context.diagnostics.Error("Syntax Error: Expected method identifier", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            string name = context.currentToken.AsString();
            context.MoveNext();
            if (context.isEnd || !context.currentToken.isPunctuation || context.currentToken != '(')
            {
                context.diagnostics.Error("Syntax Error: Expected token (", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            context.MoveNext();
            while (context.TryMoveNext(out var current))
            {
                if (current.isIdentifier)
                {
                    this.m_parameters.Add(current);
                }
                else if (current.isPunctuation && current == ',')
                {
                    continue;
                }
                else if (current.isPunctuation && current == ')')
                {
                    break;
                }
                else
                {
                    this.m_parameters.Clear();
                    context.diagnostics.Error("Syntax Error: Expected token ) or parameter", current.location);
                    return AnalyserResult.Error;
                }
            }

            string[] @params = new string[this.m_parameters.Count];
            for (int i = 0; i < @params.Length; i++)
            {
                @params[i] = this.m_parameters[i].AsString();
            }

            this.m_parameters.Clear();
            MethodNode node = new(name, modifiers.ToArray(), @params, returnType);
            return context.AnalyseScoped(node);
        }
    }
}
