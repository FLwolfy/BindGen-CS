using BGCS.Language.Parsing;
namespace BGCS.Language.Cpp
{
    using BGCS.Language.Cpp.Analysers;

    /// <summary>
    /// Composes the supported C++ macro expression analyzer; complete native declarations remain the Clang parser's responsibility.
    /// </summary>
    public class CppMacroParser : ParserBase
    {
        /// <summary>
        /// Creates a parser using the shared default comment-handling policy.
        /// </summary>
        public CppMacroParser() : this(ParserOptions.@default)
        {
        }

        /// <summary>
        /// Creates a parser with the selected comment-handling policy and the dialect's supported analyzers.
        /// </summary>
        /// <param name="options">
        /// The caller-owned parsing policy retained for later parse invocations.
        /// </param>
        public CppMacroParser(ParserOptions options) : base(options)
        {
            analyzers.Add(new ExpressionAnalyser());
        }

        /// <summary>
        /// Gets the shared macro expression parser using the shared default parsing policy.
        /// </summary>
        public static readonly CppMacroParser @default = new();
    }
}
