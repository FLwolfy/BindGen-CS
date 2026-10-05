using System.Collections.Generic;
using BGCS.Language.Diagnostics;

namespace BGCS.Language.Lexing
{
    /// <summary>
    /// Retains a lexer invocation's tokens and diagnostics for subsequent parsing.
    /// </summary>
    public class LexerResult
    {
        private readonly DiagnosticBag m_diagnostics;
        private readonly List<Token>? m_tokens;
        /// <summary>
        /// Retains the lexer-owned output objects without copying them.
        /// </summary>
        /// <param name="diagnostics">
        /// Diagnostics collected during tokenization.
        /// </param>
        /// <param name="tokens">
        /// The token list, or null when tokenization failed before producing a usable stream.
        /// </param>
        public LexerResult(
            DiagnosticBag diagnostics,
            List<Token>? tokens
        ) {
            this.m_diagnostics = diagnostics;
            this.m_tokens = tokens;
        }

        /// <summary>
        /// Gets the retained token list, or null when tokenization failed.
        /// </summary>
        public List<Token>? tokens => this.m_tokens;
        /// <summary>
        /// Gets the diagnostics collected by this lexer invocation.
        /// </summary>
        public DiagnosticBag diagnostics => this.m_diagnostics;
    }
}
