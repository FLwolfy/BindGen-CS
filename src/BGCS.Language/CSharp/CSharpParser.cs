using BGCS.Language.Parsing;
namespace BGCS.Language.CSharp
{
    using BGCS.Language.CSharp.Analyzers;

    /// <summary>
    /// Composes the supported namespace, class, using, and member syntax analyzers.
    /// </summary>
    public class CSharpParser : ParserBase
    {
        /// <summary>
        /// Creates a parser using the shared default comment-handling policy.
        /// </summary>
        public CSharpParser() : this(ParserOptions.@default)
        {
        }

        /// <summary>
        /// Creates a parser with the selected comment-handling policy and the dialect's supported analyzers.
        /// </summary>
        /// <param name="options">
        /// The caller-owned parsing policy retained for later parse invocations.
        /// </param>
        public CSharpParser(ParserOptions options) : base(options)
        {
            analyzers.Add(new NamespaceAnalyzer());
            analyzers.Add(new ClassAnalyzer());
            analyzers.Add(new ClassMemberAnalyzer());
            analyzers.Add(new UsingAnalyser());
        }
    }
}
