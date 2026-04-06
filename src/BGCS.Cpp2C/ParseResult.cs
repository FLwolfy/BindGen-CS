namespace BGCS.Cpp2C
{
    using BGCS.CppAst.Model.Metadata;

    /// <summary>
    /// Defines the public class <c>ParseResult</c>.
    /// </summary>
    public class ParseResult
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ParseResult"/>.
        /// </summary>
        public ParseResult(CppCompilation compilation)
        {
            Compilation = compilation;
        }

        /// <summary>
        /// Gets or sets <c>Compilation</c>.
        /// </summary>
        public CppCompilation Compilation { get; set; }
    }
}
