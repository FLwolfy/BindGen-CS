namespace BGCS.Language.Cpp
{
    /// <summary>
    /// Defines the public class <c>CppParser</c>.
    /// </summary>
    public class CppParser : ParserBase
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CppParser"/>.
        /// </summary>
        public CppParser() : this(ParserOptions.Default)
        {
        }

        /// <summary>
        /// Executes public operation <c>CppParser</c>.
        /// </summary>
        public CppParser(ParserOptions options) : base(options)
        {
        }
    }
}
