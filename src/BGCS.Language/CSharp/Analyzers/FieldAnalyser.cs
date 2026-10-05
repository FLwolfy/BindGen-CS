using System.Linq;
using BGCS.Language.Lexing;
using BGCS.Language.Parsing;

namespace BGCS.Language.CSharp.Analyzers
{
    using System.Collections.Generic;

    using BGCS.Language.CSharp.Nodes;

    /// <summary>
    /// Recognizes supported field declarations, retaining initializer source text without evaluating it.
    /// </summary>
    public class FieldAnalyser : IMemberSyntaxAnalyzer
    {
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
            if (context.isEnd || (!context.currentToken.isIdentifier && !context.currentToken.isKeyword))
            {
                context.diagnostics.Error("Syntax Error: Expected field type", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            string type = context.currentToken.AsString();
            context.MoveNext();
            if (context.isEnd || !context.currentToken.isIdentifier)
            {
                context.diagnostics.Error("Syntax Error: Expected field identifier", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            string name = context.currentToken.AsString();
            context.MoveNext();
            if (context.isEnd)
            {
                context.diagnostics.Error("Syntax Error: ; expected", null);
                return AnalyserResult.Error;
            }

            if (context.currentToken.isPunctuation && context.currentToken == ';')
            {
                context.MoveNext();
                FieldNode node = new(type, name, modifiers.ToArray(), null);
                context.AppendNode(node);
                return AnalyserResult.Success;
            }

            if (context.currentToken.isOperator && context.currentToken == '=')
            {
                context.MoveNext();
                if (context.isEnd)
                {
                    context.diagnostics.Error("Syntax Error: Expected expression for field", null);
                    return AnalyserResult.Error;
                }

                if (!context.currentToken.isIdentifier)
                {
                    context.diagnostics.Error("Syntax Error: Expected expression for field", context.currentToken.location);
                    return AnalyserResult.Error;
                }

                string expression = context.currentToken.AsString();
                context.MoveNext();
                if (context.isEnd)
                {
                    context.diagnostics.Error("Syntax Error: ; expected", null);
                    return AnalyserResult.Error;
                }

                if (!context.currentToken.isPunctuation || context.currentToken != ';')
                {
                    context.diagnostics.Error("Syntax Error: ; expected", context.currentToken.location);
                    return AnalyserResult.Error;
                }

                context.MoveNext();
                FieldNode node = new(type, name, modifiers.ToArray(), expression);
                context.AppendNode(node);
                return AnalyserResult.Success;
            }

            context.diagnostics.Error("Syntax Error: ; expected or expression", context.currentToken.location);
            return AnalyserResult.Error;
        }
    }
}
