namespace BGCS.Language.Diagnostics
{
    /// <summary>
    /// Classifies source diagnostics as information, a recoverable warning, or a parse-preventing error.
    /// </summary>
    public enum LogMessageType
    {
        /// <summary>
        /// Informational lexer or parser feedback.
        /// </summary>
        Information = 1,
        /// <summary>
        /// A recoverable language diagnostic.
        /// </summary>
        Warning = 2,
        /// <summary>
        /// A language error that prevents successful processing.
        /// </summary>
        Error = 3,
    }
}
