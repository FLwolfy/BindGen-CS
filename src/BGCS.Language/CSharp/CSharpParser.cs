namespace BGCS.Language.CSharp
{
    using BGCS.Language.CSharp.Analyzers;

    /// <summary>
    /// Defines the public class <c>CSharpParser</c>.
    /// </summary>
    public class CSharpParser : ParserBase
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CSharpParser"/>.
        /// </summary>
        public CSharpParser() : this(ParserOptions.Default)
        {
        }

        /// <summary>
        /// Executes public operation <c>CSharpParser</c>.
        /// </summary>
        public CSharpParser(ParserOptions options) : base(options)
        {
            analyzers.Add(new NamespaceAnalyzer());
            analyzers.Add(new ClassAnalyzer());
            analyzers.Add(new ClassMemberAnalyzer());
            analyzers.Add(new UsingAnalyser());
        }
    }
}
