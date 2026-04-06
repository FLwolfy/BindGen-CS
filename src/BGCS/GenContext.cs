namespace BGCS
{
    using BGCS.Core;
    using BGCS.CppAst.Model.Metadata;

    /// <summary>
    /// Defines the public class <c>GenContext</c>.
    /// </summary>
    public class GenContext
    {
        /// <summary>
        /// Initializes a new instance of <see cref="GenContext"/>.
        /// </summary>
        public GenContext(ParseResult result, string filePath, ICodeWriter codeWriter)
        {
            ParseResult = result;
            Compilation = result.Compilation;
            FilePath = filePath;
            Writer = codeWriter;
        }

        /// <summary>
        /// Gets or sets <c>ParseResult</c>.
        /// </summary>
        public ParseResult ParseResult { get; set; }

        /// <summary>
        /// Gets or sets <c>Compilation</c>.
        /// </summary>
        public CppCompilation Compilation { get; set; }

        /// <summary>
        /// Gets or sets <c>FilePath</c>.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets <c>Writer</c>.
        /// </summary>
        public ICodeWriter Writer { get; set; }
    }
}
