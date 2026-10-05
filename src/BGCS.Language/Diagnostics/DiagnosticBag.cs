using System.Collections.Generic;

namespace BGCS.Language.Diagnostics
{
    using System;
    using System.Text;

    /// <summary>
    /// Accumulates source diagnostics in emission order and retains whether any error has been reported.
    /// </summary>
    public class DiagnosticBag
    {
        private readonly List<DiagnosticMessage> m_messages = new();
        private readonly IReadOnlyList<DiagnosticMessage> m_readOnlyMessages;

        /// <summary>
        /// Creates an empty diagnostic collection with a live, read-only view of its entries.
        /// </summary>
        public DiagnosticBag()
        {
            m_readOnlyMessages = m_messages.AsReadOnly();
        }
        /// <summary>
        /// Gets the diagnostics in their original emission order.
        /// </summary>
        public IReadOnlyList<DiagnosticMessage> messages => m_readOnlyMessages;
        /// <summary>
        /// Gets whether an error-severity diagnostic has been added during this bag's lifetime.
        /// </summary>
        public bool hasErrors { get; private set; }

        /// <summary>
        /// Appends an information diagnostic at the supplied source location.
        /// </summary>
        /// <param name="message">
        /// The human-readable diagnostic text.
        /// </param>
        /// <param name="location">
        /// The source location, or null when no location is available.
        /// </param>
        public void Info(
            string message,
            SourceLocation? location = null
        ) {
            LogMessage(LogMessageType.Information, message, location);
        }

        /// <summary>
        /// Appends a warning diagnostic at the supplied source location.
        /// </summary>
        /// <param name="message">
        /// The human-readable diagnostic text.
        /// </param>
        /// <param name="location">
        /// The source location, or null when no location is available.
        /// </param>
        public void Warning(
            string message,
            SourceLocation? location = null
        ) {
            LogMessage(LogMessageType.Warning, message, location);
        }

        /// <summary>
        /// Appends an error diagnostic at the supplied source location.
        /// </summary>
        /// <param name="message">
        /// The human-readable diagnostic text.
        /// </param>
        /// <param name="location">
        /// The source location, or null when no location is available.
        /// </param>
        public void Error(
            string message,
            SourceLocation? location = null
        ) {
            LogMessage(LogMessageType.Error, message, location);
        }

        /// <summary>
        /// Appends an existing diagnostic and marks the bag as erroneous when its severity is Error.
        /// </summary>
        /// <param name="message">
        /// The diagnostic value to copy into this bag.
        /// </param>
        public void Log(DiagnosticMessage message)
        {
            if (message.type == LogMessageType.Error)
            {
                this.hasErrors = true;
            }

            this.m_messages.Add(message);
        }

        /// <summary>
        /// Appends this bag's diagnostics to another bag in emission order.
        /// </summary>
        /// <param name="dest">
        /// The destination bag; its existing entries are preserved.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The destination is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The destination is this bag; copying into itself would duplicate its diagnostics.
        /// </exception>
        public void CopyTo(DiagnosticBag dest)
        {
            if (dest == null)
                throw new ArgumentNullException(nameof(dest));
            if (ReferenceEquals(this, dest))
                throw new ArgumentException("A diagnostic bag cannot copy its entries into itself.", nameof(dest));
            foreach (var diagnosticMessage in this.messages)
            {
                dest.Log(diagnosticMessage);
            }
        }

        /// <summary>
        /// Adds a structured diagnostic and updates the error state when necessary.
        /// </summary>
        /// <param name="type">Diagnostic severity.</param>
        /// <param name="message">Diagnostic message.</param>
        /// <param name="location">Optional source location; null produces an unspecified location.</param>
        protected void LogMessage(
            LogMessageType type,
            string message,
            SourceLocation? location = null
        ) {
            // Try to recover a proper location
            var locationResolved = location ?? new SourceLocation(); // In case we have an unexpected BuilderException, use this location instead
            Log(new DiagnosticMessage(type, message, locationResolved));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var diagnostics = new StringBuilder();
            foreach (var message in this.messages)
            {
                diagnostics.AppendLine(message.ToString());
            }

            return diagnostics.ToString();
        }
    }
}
