namespace BGCS.Language
{
    /// <summary>
    /// Defines the public class <c>ParserOptions</c>.
    /// </summary>
    public class ParserOptions
    {
        /// <summary>
        /// Executes public operation <c>new</c>.
        /// </summary>
        public static readonly ParserOptions Default = new();

        /// <summary>
        /// Gets or sets <c>ParseComments</c>.
        /// </summary>
        public bool ParseComments { get; set; }
    }
}
