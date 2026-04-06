namespace BGCS.Language
{
    /// <summary>
    /// Defines the public class <c>LexerResult</c>.
    /// </summary>
    public class LexerResult
    {
        private readonly DiagnosticBag diagnostics;
        private readonly List<Token> tokens;

        /// <summary>
        /// Initializes a new instance of <see cref="LexerResult"/>.
        /// </summary>
        public LexerResult(DiagnosticBag diagnostics, List<Token> tokens)
        {
            this.diagnostics = diagnostics;
            this.tokens = tokens;
        }

        /// <summary>
        /// Exposes public member <c>tokens</c>.
        /// </summary>
        public List<Token> Tokens => tokens;

        /// <summary>
        /// Exposes public member <c>diagnostics</c>.
        /// </summary>
        public DiagnosticBag Diagnostics => diagnostics;
    }
}
