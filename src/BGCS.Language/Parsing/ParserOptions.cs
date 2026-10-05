namespace BGCS.Language.Parsing
{
    /// <summary>
    /// Controls whether syntax analyzers receive comment tokens.
    /// </summary>
    public class ParserOptions
    {
        /// <summary>
        /// Gets the shared mutable parsing policy, with comment parsing disabled initially.
        /// </summary>
        public static readonly ParserOptions @default = new();
        /// <summary>
        /// Gets or sets whether scoped analysis dispatches comment tokens instead of skipping them.
        /// </summary>
        public bool parseComments { get; set; }
    }
}
