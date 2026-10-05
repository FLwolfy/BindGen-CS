using BGCS.Language.Diagnostics;
using BGCS.Language.Syntax;
namespace BGCS.Language.Parsing
{
    /// <summary>
    /// Returns a parsed syntax tree together with the diagnostics produced while constructing it.
    /// </summary>
    public class ParserResult
    {
        private readonly SyntaxTree? m_tree;
        private readonly DiagnosticBag m_diagnostics;
        /// <summary>
        /// Retains the parser's tree and diagnostics without copying either object.
        /// </summary>
        /// <param name="tree">The parsed tree, or null when parsing could not create one.</param>
        /// <param name="diagnostics">The diagnostic collection belonging to this parse operation.</param>
        public ParserResult(
            SyntaxTree? tree,
            DiagnosticBag diagnostics
        ) {
            this.m_tree = tree;
            this.m_diagnostics = diagnostics;
        }

        /// <summary>
        /// Gets the parsed tree, or null when construction failed before a tree was available.
        /// </summary>
        public SyntaxTree? syntaxTree => this.m_tree;
        /// <summary>
        /// Gets the retained diagnostics; callers can inspect errors before consuming the tree.
        /// </summary>
        public DiagnosticBag diagnostics => this.m_diagnostics;
    }
}
