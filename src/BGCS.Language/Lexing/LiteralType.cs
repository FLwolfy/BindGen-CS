namespace BGCS.Language.Lexing
{
    /// <summary>
    /// Classifies parsed scalar literal carriers during language analysis.
    /// </summary>
    public enum LiteralType : byte
    {
        /// <summary>
        /// The literal category is unavailable.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// A numeric literal.
        /// </summary>
        Number = 1,
        /// <summary>
        /// A text literal.
        /// </summary>
        String = 2,
        /// <summary>
        /// A character literal.
        /// </summary>
        Char = 3,
    }
}
