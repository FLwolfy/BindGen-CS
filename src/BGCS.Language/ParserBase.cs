namespace BGCS.Language
{
    /// <summary>
    /// Defines the public class <c>ParserBase</c>.
    /// </summary>
    public class ParserBase
    {
        protected readonly List<ISyntaxAnalyzer> analyzers = new();
        protected readonly Lexer lexer = new();
        protected readonly ParserOptions options;

        /// <summary>
        /// Initializes a new instance of <see cref="ParserBase"/>.
        /// </summary>
        public ParserBase(ParserOptions options)
        {
            this.options = options;
        }

        /// <summary>
        /// Adds data or behavior through <c>AddAnalyser</c>.
        /// </summary>
        public virtual void AddAnalyser(ISyntaxAnalyzer analyzer)
        {
            analyzers.Add(analyzer);
        }

        /// <summary>
        /// Executes public operation <c>Parse</c>.
        /// </summary>
        public virtual ParserResult Parse(string input, string filename)
        {
            var lexerResult = lexer.Tokenize(input, filename);

            var diagnostics = lexerResult.Diagnostics;

            if (diagnostics.HasErrors)
                return new ParserResult(null, diagnostics);

            RootNode root = new();
            ParserContext context = new(root, options, analyzers, lexerResult.Tokens, diagnostics);

            while (!context.IsEnd)
            {
                int before = context.CurrentTokenIndex;
                var result = context.AnalyzeCurrent();
                if (result != AnalyserResult.Success)
                {
                    return new ParserResult(null, diagnostics);
                }

                if (!context.IsEnd && context.CurrentTokenIndex == before)
                {
                    diagnostics.Error("Parser made no progress.");
                    return new ParserResult(null, diagnostics);
                }
            }

            if (context.ScopeStack.Count != 0)
            {
                for (int i = 0; i < context.ScopeStack.Count; i++)
                    diagnostics.Error("Syntax Error: } expected");
                return new ParserResult(null, diagnostics);
            }

            return new ParserResult(new SyntaxTree(root), diagnostics);
        }
    }
}
