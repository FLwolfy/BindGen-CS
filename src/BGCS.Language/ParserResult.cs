namespace BGCS.Language
{
    /// <summary>
    /// Defines the public class <c>ParserResult</c>.
    /// </summary>
    public class ParserResult
    {
        private readonly SyntaxTree? tree;
        private readonly DiagnosticBag diagnostics;

        /// <summary>
        /// Initializes a new instance of <see cref="ParserResult"/>.
        /// </summary>
        public ParserResult(SyntaxTree? tree, DiagnosticBag diagnostics)
        {
            this.tree = tree;
            this.diagnostics = diagnostics;
        }

        /// <summary>
        /// Exposes public member <c>tree</c>.
        /// </summary>
        public SyntaxTree? SyntaxTree => tree;

        /// <summary>
        /// Exposes public member <c>diagnostics</c>.
        /// </summary>
        public DiagnosticBag Diagnostics => diagnostics;
    }
}
