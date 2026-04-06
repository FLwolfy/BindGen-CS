namespace BGCS.Language
{
    using System;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>DiagnosticBag</c>.
    /// </summary>
    public class DiagnosticBag
    {
        private readonly List<DiagnosticMessage> messages = new();

        /// <summary>
        /// Exposes public member <c>messages</c>.
        /// </summary>
        public IReadOnlyList<DiagnosticMessage> Messages => messages;

        /// <summary>
        /// Gets or sets <c>HasErrors</c>.
        /// </summary>
        public bool HasErrors { get; private set; }

        /// <summary>
        /// Executes public operation <c>Info</c>.
        /// </summary>
        public void Info(string message, SourceLocation? location = null)
        {
            LogMessage(LogMessageType.Information, message, location);
        }

        /// <summary>
        /// Executes public operation <c>Warning</c>.
        /// </summary>
        public void Warning(string message, SourceLocation? location = null)
        {
            LogMessage(LogMessageType.Warning, message, location);
        }

        /// <summary>
        /// Executes public operation <c>Error</c>.
        /// </summary>
        public void Error(string message, SourceLocation? location = null)
        {
            LogMessage(LogMessageType.Error, message, location);
        }

        /// <summary>
        /// Executes public operation <c>Log</c>.
        /// </summary>
        public void Log(DiagnosticMessage message)
        {
            if (message.Type == LogMessageType.Error)
            {
                HasErrors = true;
            }

            messages.Add(message);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(DiagnosticBag dest)
        {
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            foreach (var diagnosticMessage in Messages)
            {
                dest.Log(diagnosticMessage);
            }
        }

        protected void LogMessage(LogMessageType type, string message, SourceLocation? location = null)
        {
            // Try to recover a proper location
            var locationResolved = location ?? new SourceLocation(); // In case we have an unexpected BuilderException, use this location instead
            Log(new DiagnosticMessage(type, message, locationResolved));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var diagnostics = new StringBuilder();

            foreach (var message in Messages)
            {
                diagnostics.AppendLine(message.ToString());
            }

            return diagnostics.ToString();
        }
    }
}
