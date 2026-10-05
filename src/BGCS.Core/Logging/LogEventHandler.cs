namespace BGCS.Core.Logging;

/// <summary>
/// Receives one diagnostic selected for live presentation on the component's owner thread.
/// </summary>
/// <param name="severity">
/// The importance of the diagnostic.
/// </param>
/// <param name="message">
/// The complete diagnostic text.
/// </param>
public delegate void LogEventHandler(
    LogSeverity severity,
    string message
);
