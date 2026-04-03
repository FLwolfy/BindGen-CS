namespace BGCS.Language.Cpp
{
    using BGCS.Language.Cpp.Analysers;

    public class CppMacroParser : ParserBase
    {
        public CppMacroParser() : this(ParserOptions.Default)
        {
        }

        public CppMacroParser(ParserOptions options) : base(options)
        {
            analyzers.Add(new ExpressionAnalyser());
        }

        public static readonly CppMacroParser Default = new();
    }
}
