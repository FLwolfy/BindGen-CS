namespace BGCS.Core.Logging
{
    /// <summary>
    /// Orders generation diagnostics from tracing through unrecoverable failures for threshold-based reporting.
    /// </summary>
    public enum LogSeverity
    {
        /// <summary>
        /// Fine-grained generation tracing.
        /// </summary>
        Trace = 0,
        /// <summary>
        /// Details useful while diagnosing generation.
        /// </summary>
        Debug = 1,
        /// <summary>
        /// Normal progress and contextual information.
        /// </summary>
        Information = 2,
        /// <summary>
        /// A recoverable condition requiring review.
        /// </summary>
        Warning = 3,
        /// <summary>
        /// A condition that prevents a supported generation result.
        /// </summary>
        Error = 4,
        /// <summary>
        /// A failure that prevents safe continuation.
        /// </summary>
        Critical = 5
    }
}
