namespace BGCS.Cpp2C
{
    using BGCS.CppAst.Model.Metadata;

    public class ParseResult
    {
        public ParseResult(CppCompilation compilation)
        {
            Compilation = compilation;
        }

        public CppCompilation Compilation { get; set; }
    }
}
