using System.Collections.Generic;

namespace BGCS.Cpp2C.Analysis
{
    using BGCS.CppAst.Model.Metadata;

    /// <summary>
    /// Retains bridge-analysis inputs while borrowing the native compilation and its parser-owned data.
    /// </summary>
    public class ParseResult
    {
        /// <summary>
        /// Captures the compilation and the root header list for one bridge-analysis attempt.
        /// </summary>
        /// <param name="compilation">
        /// The borrowed compilation, alive until bridge analysis freezes its IR.
        /// </param>
        /// <param name="entryFiles">
        /// The root header list retained without copying, or null for an empty list.
        /// </param>
        public ParseResult(
            CppCompilation compilation,
            IReadOnlyList<string>? entryFiles = null
        ) {
            this.compilation = compilation;
            this.entryFiles = entryFiles ?? [];
        }

        /// <summary>
        /// Gets or sets the native compilation borrowed by this bridge-analysis attempt.
        /// </summary>
        public CppCompilation compilation { get; set; }
        /// <summary>
        /// Gets the root headers supplied directly to the generator.
        /// </summary>
        public IReadOnlyList<string> entryFiles { get; }
    }
}
