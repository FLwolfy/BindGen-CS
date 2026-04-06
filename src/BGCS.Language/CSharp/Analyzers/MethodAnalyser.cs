namespace BGCS.Language.CSharp.Analyzers
{
    using BGCS.Language.CSharp.Nodes;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>MethodAnalyser</c>.
    /// </summary>
    public class MethodAnalyser : IMemberSyntaxAnalyzer
    {
        private readonly List<Token> parameters = new();

        /// <summary>
        /// Executes public operation <c>Analyze</c>.
        /// </summary>
        public AnalyserResult Analyze(ParserContext context, IReadOnlyList<KeywordType> modifiers)
        {
            if (!context.SeekInBounds(2))
            {
                return AnalyserResult.Unrecognised;
            }

            while (!context.IsEnd &&
                   context.CurrentToken.IsKeyword &&
                   (context.CurrentToken == KeywordType.Public ||
                    context.CurrentToken == KeywordType.Internal ||
                    context.CurrentToken == KeywordType.Protected ||
                    context.CurrentToken == KeywordType.Private ||
                    context.CurrentToken == KeywordType.Readonly ||
                    context.CurrentToken == KeywordType.Static ||
                    context.CurrentToken == KeywordType.Const ||
                    context.CurrentToken == KeywordType.Unsafe))
            {
                context.MoveNext();
            }

            if (context.IsEnd || (!context.CurrentToken.IsIdentifier && !context.CurrentToken.IsKeyword))
            {
                context.Diagnostics.Error("Syntax Error: Expected method return type", context.IsEnd ? null : context.CurrentToken.Location);
                return AnalyserResult.Error;
            }

            string returnType = context.CurrentToken.AsString();

            context.MoveNext();

            if (context.IsEnd || !context.CurrentToken.IsIdentifier)
            {
                context.Diagnostics.Error("Syntax Error: Expected method identifier", context.IsEnd ? null : context.CurrentToken.Location);
                return AnalyserResult.Error;
            }

            string name = context.CurrentToken.AsString();

            context.MoveNext();

            if (context.IsEnd || !context.CurrentToken.IsPunctuation || context.CurrentToken != '(')
            {
                context.Diagnostics.Error("Syntax Error: Expected token (", context.IsEnd ? null : context.CurrentToken.Location);
                return AnalyserResult.Error;
            }

            context.MoveNext();

            while (context.TryMoveNext(out var current))
            {
                if (current.IsIdentifier)
                {
                    parameters.Add(current);
                }
                else if (current.IsPunctuation && current == ',')
                {
                    continue;
                }
                else if (current.IsPunctuation && current == ')')
                {
                    break;
                }
                else
                {
                    parameters.Clear();
                    context.Diagnostics.Error("Syntax Error: Expected token ) or parameter", current.Location);
                    return AnalyserResult.Error;
                }
            }

            string[] @params = new string[parameters.Count];
            for (int i = 0; i < @params.Length; i++)
            {
                @params[i] = parameters[i].AsString();
            }
            parameters.Clear();

            MethodNode node = new(name, modifiers.ToArray(), @params, returnType);

            return context.AnalyseScoped(node);
        }
    }
}
