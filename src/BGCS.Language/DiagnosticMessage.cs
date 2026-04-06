namespace BGCS.Language
{
    /// <summary>
    /// Defines the public struct <c>DiagnosticMessage</c>.
    /// </summary>
    public struct DiagnosticMessage
    {
        /// <summary>
        /// Exposes public member <c>Type</c>.
        /// </summary>
        public LogMessageType Type;
        /// <summary>
        /// Exposes public member <c>Text</c>.
        /// </summary>
        public string Text;
        /// <summary>
        /// Exposes public member <c>Location</c>.
        /// </summary>
        public SourceLocation Location;

        /// <summary>
        /// Initializes a new instance of <see cref="DiagnosticMessage"/>.
        /// </summary>
        public DiagnosticMessage(LogMessageType type, string text, SourceLocation location)
        {
            Type = type;
            Text = text;
            Location = location;
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"{Type}: {Text}, {Location}";
        }
    }
}
