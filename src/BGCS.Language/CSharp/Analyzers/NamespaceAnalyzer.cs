using BGCS.Language.CSharp.Nodes;
using BGCS.Language.Lexing;
using BGCS.Language.Parsing;

namespace BGCS.Language.CSharp.Analyzers;

/// <summary>
/// Recognizes supported namespace declarations and coordinates their document scope.
/// </summary>
public class NamespaceAnalyzer : ISyntaxAnalyzer
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

        if (context.currentToken == KeywordType.Namespace)
        {
            context.MoveNext();
            if (!context.currentToken.isIdentifier)
            {
                context.diagnostics.Error("Syntax Error: Expected namespace identifier", context.currentToken.location);
                return AnalyserResult.Error;
            }

            NamespaceNode node = new(context.currentToken.AsString());
            context.MoveNext();
            return context.AnalyseScoped(node);
        }

        return AnalyserResult.Unrecognised;
    }
}
