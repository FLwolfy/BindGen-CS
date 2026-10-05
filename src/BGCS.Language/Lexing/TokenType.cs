namespace BGCS.Language.Lexing
{
    /// <summary>
    /// Classifies source ranges as identifiers, keywords, delimiters, operators, literals or comments.
    /// </summary>
    public enum TokenType : byte
    {
        /// <summary>
        /// The token category is unavailable.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// A declaration or reference identifier.
        /// </summary>
        Identifier = 1,
        /// <summary>
        /// A recognized language keyword.
        /// </summary>
        Keyword = 2,
        /// <summary>
        /// A syntax delimiter.
        /// </summary>
        Punctuation = 3,
        /// <summary>
        /// An expression operator.
        /// </summary>
        Operator = 4,
        /// <summary>
        /// A literal value.
        /// </summary>
        Literal = 5,
        /// <summary>
        /// A source comment.
        /// </summary>
        Comment = 6,
    }
}
