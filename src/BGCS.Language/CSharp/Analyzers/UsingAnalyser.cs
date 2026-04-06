namespace BGCS.Language.CSharp.Analyzers
{
    using System;
    using BGCS.Language;
    using BGCS.Language.CSharp.Nodes;

    /// <summary>
    /// Defines the public class <c>UsingAnalyser</c>.
    /// </summary>
    public class UsingAnalyser : ISyntaxAnalyzer
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

            if (context.CurrentToken == KeywordType.Using)
            {
                context.MoveNext();

                if (!context.CurrentToken.IsIdentifier)
                {
                    context.Diagnostics.Error("Syntax Error: Expected using identifier", context.CurrentToken.Location);
                    return AnalyserResult.Error;
                }

                var name = context.CurrentToken.AsString();

                context.MoveNext();

                if (!context.CurrentToken.IsPunctuation || context.CurrentToken != ';')
                {
                    context.Diagnostics.Error("Syntax Error: ; expected", context.CurrentToken.Location);
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
