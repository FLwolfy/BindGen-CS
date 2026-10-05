// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Diagnostics;

/// <summary>
/// Accumulates parser diagnostics in occurrence order and tracks whether any error prevents analysis.
/// </summary>
public class CppDiagnosticBag
{
    private readonly List<CppDiagnosticMessage> m_messages;
    /// <summary>
    /// Creates an empty diagnostic collection with no error state.
    /// </summary>
    public CppDiagnosticBag()
    {
        this.m_messages = [];
    }

    /// <summary>
    /// Removes all recorded diagnostics and resets the error state.
    /// </summary>
    public void Clear()
    {
        this.m_messages.Clear();
        this.hasErrors = false;
    }

    /// <summary>
    /// Gets the live diagnostic view in occurrence order; this view is not a snapshot.
    /// </summary>
    public IReadOnlyList<CppDiagnosticMessage> messages => this.m_messages;
    /// <summary>
    /// Gets whether an error has been added since the last clear operation.
    /// </summary>
    public bool hasErrors { get; private set; }

    /// <summary>
    /// Appends an informational message without changing the error state.
    /// </summary>
    /// <param name="message">The non-null diagnostic text.</param>
    /// <param name="location">The source location, or null when no source position is available.</param>
    public void Info(
        string message,
        CppSourceLocation? location = null
    ) {
        LogMessage(CppLogMessageType.Info, message, location);
    }

    /// <summary>
    /// Appends a recoverable warning without changing the error state.
    /// </summary>
    /// <param name="message">The non-null diagnostic text.</param>
    /// <param name="location">The source location, or null when no source position is available.</param>
    public void Warning(
        string message,
        CppSourceLocation? location = null
    ) {
        LogMessage(CppLogMessageType.Warning, message, location);
    }

    /// <summary>
    /// Appends a parsing error and marks the collection as containing errors.
    /// </summary>
    /// <param name="message">The non-null diagnostic text.</param>
    /// <param name="location">The source location, or null when no source position is available.</param>
    public void Error(
        string message,
        CppSourceLocation? location = null
    ) {
        LogMessage(CppLogMessageType.Error, message, location);
    }

    /// <summary>
    /// Retains a diagnostic and updates the error state from its severity.
    /// </summary>
    /// <param name="message">The diagnostic object to retain.</param>
    /// <exception cref="ArgumentNullException">The diagnostic is null.</exception>
    public void Log(CppDiagnosticMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        if (message.type == CppLogMessageType.Error)
        {
            this.hasErrors = true;
        }

        this.m_messages.Add(message);
    }

    /// <summary>
    /// Appends these diagnostics to another bag in occurrence order, updating its error state.
    /// </summary>
    /// <param name="dest">The non-null destination receiving the same diagnostic objects.</param>
    /// <exception cref="ArgumentNullException">The destination is null.</exception>
    public void CopyTo(CppDiagnosticBag dest)
    {
        if (dest == null)
            throw new ArgumentNullException(nameof(dest));
        foreach (var cppDiagnosticMessage in this.messages)
        {
            dest.Log(cppDiagnosticMessage);
        }
    }

    /// <summary>
    /// Adds a structured diagnostic and updates the error state when necessary.
    /// </summary>
    /// <param name="type">Diagnostic severity.</param>
    /// <param name="message">Diagnostic message.</param>
    /// <param name="location">Optional source location; null produces an unspecified location.</param>
    protected void LogMessage(
        CppLogMessageType type,
        string message,
        CppSourceLocation? location = null
    ) {
        // Try to recover a proper location
        var locationResolved = location ?? new CppSourceLocation(); // In case we have an unexpected BuilderException, use this location instead
        Log(new CppDiagnosticMessage(type, message, locationResolved));
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
