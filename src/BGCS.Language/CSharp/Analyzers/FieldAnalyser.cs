namespace BGCS.Language.CSharp.Analyzers
{
    using System.Collections.Generic;
    using BGCS.Language;
    using BGCS.Language.CSharp.Nodes;

    public class FieldAnalyser : IMemberSyntaxAnalyzer
    {
        public AnalyserResult Analyze(ParserContext context, IReadOnlyList<KeywordType> modifiers)
        {
            if (context.IsEnd || (!context.CurrentToken.IsIdentifier && !context.CurrentToken.IsKeyword))
            {
                context.Diagnostics.Error("Syntax Error: Expected field type", context.IsEnd ? null : context.CurrentToken.Location);
                return AnalyserResult.Error;
            }

            string type = context.CurrentToken.AsString();
            context.MoveNext();

            if (context.IsEnd || !context.CurrentToken.IsIdentifier)
            {
                context.Diagnostics.Error("Syntax Error: Expected field identifier", context.IsEnd ? null : context.CurrentToken.Location);
                return AnalyserResult.Error;
            }

            string name = context.CurrentToken.AsString();
            context.MoveNext();

            if (context.IsEnd)
            {
                context.Diagnostics.Error("Syntax Error: ; expected", null);
                return AnalyserResult.Error;
            }

            if (context.CurrentToken.IsPunctuation && context.CurrentToken == ';')
            {
                context.MoveNext();

                FieldNode node = new(type, name, modifiers.ToArray(), null);
                context.AppendNode(node);
                return AnalyserResult.Success;
            }

            if (context.CurrentToken.IsOperator && context.CurrentToken == '=')
            {
                context.MoveNext();
                if (context.IsEnd)
                {
                    context.Diagnostics.Error("Syntax Error: Expected expression for field", null);
                    return AnalyserResult.Error;
                }

                if (!context.CurrentToken.IsIdentifier)
                {
                    context.Diagnostics.Error("Syntax Error: Expected expression for field", context.CurrentToken.Location);
                    return AnalyserResult.Error;
                }

                string expression = context.CurrentToken.AsString();

                context.MoveNext();

                if (context.IsEnd)
                {
                    context.Diagnostics.Error("Syntax Error: ; expected", null);
                    return AnalyserResult.Error;
                }

                if (!context.CurrentToken.IsPunctuation || context.CurrentToken != ';')
                {
                    context.Diagnostics.Error("Syntax Error: ; expected", context.CurrentToken.Location);
                    return AnalyserResult.Error;
                }

                context.MoveNext();

                FieldNode node = new(type, name, modifiers.ToArray(), expression);
                context.AppendNode(node);
                return AnalyserResult.Success;
            }

            context.Diagnostics.Error("Syntax Error: ; expected or expression", context.CurrentToken.Location);
            return AnalyserResult.Error;
        }
    }
}
