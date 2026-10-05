using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BGCS.Core.Logging;

/// <summary>
/// Captures diagnostics for one operation and exposes optional live notifications without choosing a presentation.
/// </summary>
/// <remarks>
/// Calls and subscriptions belong to the component's owner thread. Consumers own their event subscriptions.
/// Notification verbosity never removes captured diagnostics from a result.
/// </remarks>
public abstract class LoggerBase
{
    private readonly List<LogMessage> m_messages = [];
    private readonly HashSet<LogMessage> m_messageKeys = [];
    private readonly ReadOnlyCollection<LogMessage> m_view;
    private LogSeverity m_logLevel = LogSeverity.Information;

    /// <summary>
    /// Creates an empty diagnostic lifetime for a derived component.
    /// </summary>
    protected LoggerBase() => m_view = m_messages.AsReadOnly();

    /// <summary>
    /// Gets the current operation's diagnostics in their first occurrence order.
    /// </summary>
    /// <remarks>
    /// The read-only view follows the current operation. Copy it when retaining diagnostics across operations.
    /// </remarks>
    public IReadOnlyList<LogMessage> messages => m_view;

    /// <summary>
    /// Notifies subscribers after a new diagnostic has been captured and meets the live notification threshold.
    /// </summary>
    public event LogEventHandler? LogEvent;

    /// <summary>
    /// Gets or sets the minimum severity delivered to live subscribers; stored diagnostics remain complete.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a declared severity.
    /// </exception>
    public LogSeverity logLevel
    {
        get => m_logLevel;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            m_logLevel = value;
        }
    }

    /// <summary>
    /// Records a diagnostic once per severity and text, then notifies eligible live subscribers.
    /// </summary>
    /// <param name="severity">
    /// A declared diagnostic severity.
    /// </param>
    /// <param name="message">
    /// Diagnostic text, including source or failure context when available.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The severity is not declared.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The message is null.
    /// </exception>
    public void Log(
        LogSeverity severity,
        string message
    ) => RecordDiagnostic(severity, message, logLevel);

    /// <summary>
    /// Records a failure that prevents the operation from continuing safely.
    /// </summary>
    /// <param name="message">
    /// Failure text.
    /// </param>
    public void LogCritical(string message) => Log(LogSeverity.Critical, message);

    /// <summary>
    /// Records implementation detail useful while debugging an operation.
    /// </summary>
    /// <param name="message">
    /// Debugging detail.
    /// </param>
    public void LogDebug(string message) => Log(LogSeverity.Debug, message);

    /// <summary>
    /// Records an operation failure requiring caller action.
    /// </summary>
    /// <param name="message">
    /// Error detail.
    /// </param>
    public void LogError(string message) => Log(LogSeverity.Error, message);

    /// <summary>
    /// Records operation progress or an informational outcome.
    /// </summary>
    /// <param name="message">
    /// Progress text.
    /// </param>
    public void LogInfo(string message) => Log(LogSeverity.Information, message);

    /// <summary>
    /// Records fine-grained execution detail.
    /// </summary>
    /// <param name="message">
    /// Trace detail.
    /// </param>
    public void LogTrace(string message) => Log(LogSeverity.Trace, message);

    /// <summary>
    /// Records a recoverable issue that may require caller attention.
    /// </summary>
    /// <param name="message">
    /// Warning detail.
    /// </param>
    public void LogWarn(string message) => Log(LogSeverity.Warning, message);

    /// <summary>
    /// Starts a new operation's diagnostic lifetime while retaining subscribers and verbosity.
    /// </summary>
    protected void ResetDiagnostics()
    {
        m_messages.Clear();
        m_messageKeys.Clear();
    }

    /// <summary>
    /// Records a diagnostic with an additional source-specific live notification threshold.
    /// </summary>
    /// <param name="severity">
    /// The diagnostic severity.
    /// </param>
    /// <param name="message">
    /// Diagnostic text.
    /// </param>
    /// <param name="notificationThreshold">
    /// The source threshold; both this threshold and the component threshold must be met to notify.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either severity is not declared.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The text is null.
    /// </exception>
    protected void RecordDiagnostic(
        LogSeverity severity,
        string message,
        LogSeverity notificationThreshold
    ) {
        if (!Enum.IsDefined(notificationThreshold))
            throw new ArgumentOutOfRangeException(nameof(notificationThreshold));
        var diagnostic = new LogMessage(severity, message);
        if (!m_messageKeys.Add(diagnostic))
            return;
        m_messages.Add(diagnostic);
        if (severity >= logLevel && severity >= notificationThreshold)
            LogEvent?.Invoke(severity, message);
    }
}
