namespace BGCS.Language.Cpp
{
    using BGCS.Language.Cpp.Analysers;

    /// <summary>
    /// Defines the public class <c>CppMacroParser</c>.
    /// </summary>
    public class CppMacroParser : ParserBase
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CppMacroParser"/>.
        /// </summary>
        public CppMacroParser() : this(ParserOptions.Default)
        {
        }

        /// <summary>
        /// Executes public operation <c>CppMacroParser</c>.
        /// </summary>
        public CppMacroParser(ParserOptions options) : base(options)
        {
            analyzers.Add(new ExpressionAnalyser());
        }

        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public static readonly CppMacroParser Default = new();
    }
}
