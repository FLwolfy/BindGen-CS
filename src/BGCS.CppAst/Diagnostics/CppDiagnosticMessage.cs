// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using BGCS.CppAst.Model.Metadata;
using System;

namespace BGCS.CppAst.Diagnostics;
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
    public CppDiagnosticMessage(CppLogMessageType type, string text, CppSourceLocation location)
    {
        Type = type;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Location = location;
    }

    public readonly CppLogMessageType Type;

    public readonly string Text;

    public readonly CppSourceLocation Location;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Location}: {Type.ToString().ToLowerInvariant()}: {Text}";
    }
}
