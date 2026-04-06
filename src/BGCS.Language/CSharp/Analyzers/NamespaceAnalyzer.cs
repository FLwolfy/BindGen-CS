using BGCS.Language.CSharp.Nodes;

namespace BGCS.Language.CSharp.Analyzers;
    /// <summary>
    /// Defines the public class <c>NamespaceAnalyzer</c>.
    /// </summary>
    public class NamespaceAnalyzer : ISyntaxAnalyzer
    {
        /// <summary>
        /// Executes public operation <c>Analyze</c>.
        /// </summary>
        public AnalyserResult Analyze(ParserContext context)
        {
            if (!context.SeekInBounds(2))
            {
                return AnalyserResult.Unrecognised;
            }

            if (context.CurrentToken == KeywordType.Namespace)
            {
                context.MoveNext();

                if (!context.CurrentToken.IsIdentifier)
                {
                    context.Diagnostics.Error("Syntax Error: Expected namespace identifier", context.CurrentToken.Location);
                    return AnalyserResult.Error;
                }

                NamespaceNode node = new(context.CurrentToken.AsString());
                context.MoveNext();

                return context.AnalyseScoped(node);
            }

            return AnalyserResult.Unrecognised;
        }
    }
