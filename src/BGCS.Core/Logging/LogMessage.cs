using System;

namespace BGCS.Core.Logging;

/// <summary>
/// Captures immutable diagnostic text and severity without retaining a generation service.
/// </summary>
public readonly record struct LogMessage
{
    /// <summary>
    /// Creates a diagnostic suitable for deduplication and retention in an operation result.
    /// </summary>
    /// <param name="severity">
    /// A declared diagnostic severity.
    /// </param>
    /// <param name="message">
    /// Complete diagnostic text.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The severity is not declared.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The text is null.
    /// </exception>
    public LogMessage(
        LogSeverity severity,
        string message
    ) {
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));
        ArgumentNullException.ThrowIfNull(message);
        this.severity = severity;
        this.message = message;
    }

    /// <summary>
    /// Gets the diagnostic's declared importance.
    /// </summary>
    public LogSeverity severity { get; }

    /// <summary>
    /// Gets the complete diagnostic text supplied by the reporting component.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Formats this diagnostic without writing to a process-wide output stream.
    /// </summary>
    /// <returns>
    /// A severity prefix and the stored diagnostic text.
    /// </returns>
    public override string ToString() => $"[{severity}]\t{message}";
}
