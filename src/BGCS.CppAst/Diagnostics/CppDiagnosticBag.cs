// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using BGCS.CppAst.Model.Metadata;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Diagnostics;
/// <summary>
/// Defines the public class <c>CppDiagnosticBag</c>.
/// </summary>
public class CppDiagnosticBag
{
    private readonly List<CppDiagnosticMessage> _messages;

    /// <summary>
    /// Initializes a new instance of <see cref="CppDiagnosticBag"/>.
    /// </summary>
    public CppDiagnosticBag()
    {
        _messages = [];
    }

    /// <summary>
    /// Executes public operation <c>Clear</c>.
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
        HasErrors = false;
    }

    /// <summary>
    /// Exposes public member <c>_messages</c>.
    /// </summary>
    public IReadOnlyList<CppDiagnosticMessage> Messages => _messages;

    /// <summary>
    /// Gets or sets <c>HasErrors</c>.
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>
    /// Executes public operation <c>Info</c>.
    /// </summary>
    public void Info(string message, CppSourceLocation? location = null)
    {
        LogMessage(CppLogMessageType.Info, message, location);
    }

    /// <summary>
    /// Executes public operation <c>Warning</c>.
    /// </summary>
    public void Warning(string message, CppSourceLocation? location = null)
    {
        LogMessage(CppLogMessageType.Warning, message, location);
    }

    /// <summary>
    /// Executes public operation <c>Error</c>.
    /// </summary>
    public void Error(string message, CppSourceLocation? location = null)
    {
        LogMessage(CppLogMessageType.Error, message, location);
    }

    /// <summary>
    /// Executes public operation <c>Log</c>.
    /// </summary>
    public void Log(CppDiagnosticMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (message.Type == CppLogMessageType.Error)
        {
            HasErrors = true;
        }

        _messages.Add(message);
    }

    /// <summary>
    /// Executes public operation <c>CopyTo</c>.
    /// </summary>
    public void CopyTo(CppDiagnosticBag dest)
    {
        if (dest == null) throw new ArgumentNullException(nameof(dest));
        foreach (var cppDiagnosticMessage in Messages)
        {
            dest.Log(cppDiagnosticMessage);
        }
    }

    protected void LogMessage(CppLogMessageType type, string message, CppSourceLocation? location = null)
    {
        // Try to recover a proper location
        var locationResolved = location ?? new CppSourceLocation(); // In case we have an unexpected BuilderException, use this location instead
        Log(new CppDiagnosticMessage(type, message, locationResolved));
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
