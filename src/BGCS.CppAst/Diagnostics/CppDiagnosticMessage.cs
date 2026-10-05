// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Diagnostics;

/// <summary>
/// Classifies parser diagnostics by whether declaration analysis can continue reliably.
/// </summary>
public enum CppLogMessageType
{
    /// <summary>
    /// Informational parser diagnostic.
    /// </summary>
    Info = 0,
    /// <summary>
    /// Recoverable parser diagnostic requiring attention.
    /// </summary>
    Warning = 1,
    /// <summary>
    /// Parser failure preventing reliable declaration analysis.
    /// </summary>
    Error = 2,
}

/// <summary>
/// Provides a diagnostic message for a specific location in the source code.
/// </summary>
public class CppDiagnosticMessage
{
    /// <summary>
    /// Captures a parser diagnostic and its source location without retaining a Clang cursor.
    /// </summary>
    /// <param name="type">The severity of the diagnostic.</param>
    /// <param name="text">The non-null diagnostic text.</param>
    /// <param name="location">The value describing the source position.</param>
    /// <exception cref="ArgumentNullException">The diagnostic text is null.</exception>
    public CppDiagnosticMessage(
        CppLogMessageType type,
        string text,
        CppSourceLocation location
    ) {
        this.type = type;
        this.text = text ?? throw new ArgumentNullException(nameof(text));
        this.location = location;
    }

    /// <summary>
    /// Gets the severity used to determine whether parsing failed.
    /// </summary>
    public readonly CppLogMessageType type;
    /// <summary>
    /// Gets the diagnostic text captured when the message was created.
    /// </summary>
    public readonly string text;
    /// <summary>
    /// Gets the source position; a default value indicates an unspecified location.
    /// </summary>
    public readonly CppSourceLocation location;
    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.location}: {this.type.ToString().ToLowerInvariant()}: {this.text}";
    }
}
