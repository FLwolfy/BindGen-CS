using BGCS.Language.Lexing;
using BGCS.Language.Parsing;
namespace BGCS.Language.CSharp.Analyzers
{
    using System.Collections.Generic;
    using BGCS.Language.CSharp.Nodes;

    /// <summary>
    /// Recognizes class declarations and opens their syntax scope for subsequent member analysis.
    /// </summary>
    public class ClassAnalyzer : ISyntaxAnalyzer
    {
        private readonly List<KeywordType> m_modifiers = new();
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
            if (!context.SeekInBounds(2))
            {
                return AnalyserResult.Unrecognised;
            }

            var start = context.currentTokenIndex;
            while (context.TryMoveNext(out var current))
            {
                if (current.isKeyword)
                {
                    if (current == KeywordType.Class)
                    {
                        break;
                    }
                    else if (current == KeywordType.Public || current == KeywordType.Internal || current == KeywordType.Protected || current == KeywordType.Private || current == KeywordType.Static || current == KeywordType.Unsafe)
                    {
                        this.m_modifiers.Add(current.keywordType);
                    }
                    else
                    {
                        this.m_modifiers.Clear();
                        context.MoveTo(start);
                        return AnalyserResult.Unrecognised;
                    }
                }
                else
                {
                    this.m_modifiers.Clear();
                    context.MoveTo(start);
                    return AnalyserResult.Unrecognised;
                }
            }

            if (context.isEnd || !context.currentToken.isIdentifier)
            {
                context.diagnostics.Error("Syntax Error: Expected class identifier", context.isEnd ? null : context.currentToken.location);
                return AnalyserResult.Error;
            }

            ClassNode node = new(context.currentToken.AsString(), this.m_modifiers.ToArray());
            this.m_modifiers.Clear();
            context.MoveNext();
            return context.AnalyseScoped(node);
        }
    }
}
