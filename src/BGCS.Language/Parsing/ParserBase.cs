using System;
using System.Collections.Generic;
using BGCS.Language.Lexing;
using BGCS.Language.Syntax;

namespace BGCS.Language.Parsing
{
    /// <summary>
    /// Dispatches a token stream through ordered syntax analyzers and owns the resulting parse operation.
    /// </summary>
    public class ParserBase
    {

        /// <summary>
        /// Registered syntax analyzers in the order used by parser dispatch.
        /// </summary>
        protected readonly List<ISyntaxAnalyzer> analyzers = new();

        /// <summary>
        /// Lexer owned by this parser and used for each parse operation.
        /// </summary>
        protected readonly Lexer lexer = new();

        /// <summary>
        /// Parsing policy borrowed from the caller for the lifetime of this parser.
        /// </summary>
        protected readonly ParserOptions options;
        /// <summary>
        /// Retains the parsing policy used for each invocation.
        /// </summary>
        /// <param name="options">
        /// The caller-owned policy; changes affect later invocations.
        /// </param>
        public ParserBase(ParserOptions options)
        {
            this.options = options;
        }

        /// <summary>
        /// Appends an analyzer after existing analyzers in dispatch order.
        /// </summary>
        /// <param name="analyzer">
        /// The analyzer retained by this parser; successful analysis must advance the token cursor.
        /// </param>
        public virtual void AddAnalyser(ISyntaxAnalyzer analyzer)
        {
            analyzers.Add(analyzer);
        }

        /// <summary>
        /// Creates an independent token context and returns a tree only after all tokens and scopes are consumed.
        /// </summary>
        /// <param name="input">
        /// The source text to tokenize.
        /// </param>
        /// <param name="filename">
        /// The source name attached to lexer and parser diagnostics.
        /// </param>
        /// <returns>
        /// The completed tree and diagnostics, or a null tree when lexing, analysis, scope balance, or progress validation fails.
        /// </returns>
        public virtual ParserResult Parse(
            string input,
            string filename
        ) {
            var lexerResult = lexer.Tokenize(input, filename);
            var diagnostics = lexerResult.diagnostics;
            if (diagnostics.hasErrors)
                return new ParserResult(null, diagnostics);
            RootNode root = new();
            List<Token> tokens = lexerResult.tokens ?? throw new InvalidOperationException("Lexer returned no tokens without an error diagnostic.");
            ParserContext context = new(root, options, analyzers, tokens, diagnostics);
            while (!context.isEnd)
            {
                var result = context.AnalyzeCurrent();
                if (result != AnalyserResult.Success)
                {
                    return new ParserResult(null, diagnostics);
                }
            }

            if (context.scopeDepth != 0)
            {
                for (int i = 0; i < context.scopeDepth; i++)
                    diagnostics.Error("Syntax Error: } expected");
                return new ParserResult(null, diagnostics);
            }

            return new ParserResult(new SyntaxTree(root), diagnostics);
        }
    }
}
