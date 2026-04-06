// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using BGCS.CppAst.Model.Metadata;
using System;

namespace BGCS.CppAst.Diagnostics;
/// <summary>
/// Defines values for <c>CppLogMessageType</c>.
/// </summary>
public enum CppLogMessageType
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Provides a diagnostic message for a specific location in the source code.
/// </summary>
public class CppDiagnosticMessage
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppDiagnosticMessage"/>.
    /// </summary>
    public CppDiagnosticMessage(CppLogMessageType type, string text, CppSourceLocation location)
    {
        Type = type;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Location = location;
    }

    /// <summary>
    /// Exposes public member <c>Type</c>.
    /// </summary>
    public readonly CppLogMessageType Type;

    /// <summary>
    /// Exposes public member <c>Text</c>.
    /// </summary>
    public readonly string Text;

    /// <summary>
    /// Exposes public member <c>Location</c>.
    /// </summary>
    public readonly CppSourceLocation Location;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Location}: {Type.ToString().ToLowerInvariant()}: {Text}";
    }
}
