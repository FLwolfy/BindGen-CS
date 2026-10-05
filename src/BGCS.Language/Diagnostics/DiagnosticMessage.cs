namespace BGCS.Language.Diagnostics
{
    /// <summary>
    /// Carries a diagnostic's severity, display text, and source location as a value.
    /// </summary>
    public struct DiagnosticMessage
    {
        /// <summary>
        /// The diagnostic severity used when updating a bag's error state.
        /// </summary>
        public LogMessageType type;
        /// <summary>
        /// The human-readable diagnostic message.
        /// </summary>
        public string text;
        /// <summary>
        /// The source position associated with the diagnostic.
        /// </summary>
        public SourceLocation location;
        /// <summary>
        /// Creates a diagnostic value without retaining any parser context.
        /// </summary>
        /// <param name="type">
        /// The diagnostic severity.
        /// </param>
        /// <param name="text">
        /// The human-readable message.
        /// </param>
        /// <param name="location">
        /// The source position reported by the producer.
        /// </param>
        public DiagnosticMessage(
            LogMessageType type,
            string text,
            SourceLocation location
        ) {
            this.type = type;
            this.text = text;
            this.location = location;
        }

        /// <summary>
        /// Formats severity, diagnostic text, and source location for display.
        /// </summary>
        /// <returns>
        /// The formatted diagnostic representation.
        /// </returns>
        public override string ToString()
        {
            return $"{this.type}: {this.text}, {this.location}";
        }
    }
}
