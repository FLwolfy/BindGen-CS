using BGCS.Language.Lexing;
using BGCS.Language.Parsing;
namespace BGCS.Language.CSharp.Analyzers
{

    using BGCS.Language.CSharp.Nodes;

    /// <summary>
    /// Recognizes namespace-import declarations and appends their retained spelling to the current syntax scope.
    /// </summary>
    public class UsingAnalyser : ISyntaxAnalyzer
    {
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

            if (context.currentToken == KeywordType.Using)
            {
                context.MoveNext();
                if (!context.currentToken.isIdentifier)
                {
                    context.diagnostics.Error("Syntax Error: Expected using identifier", context.currentToken.location);
                    return AnalyserResult.Error;
                }

                var name = context.currentToken.AsString();
                context.MoveNext();
                if (!context.currentToken.isPunctuation || context.currentToken != ';')
                {
                    context.diagnostics.Error("Syntax Error: ; expected", context.currentToken.location);
                    return AnalyserResult.Error;
                }

                context.MoveNext();
                UsingNode node = new(name);
                context.AppendNode(node);
                return AnalyserResult.Success;
            }

            return AnalyserResult.Unrecognised;
        }
    }
}
