using System;
using BGCS.Contracts;
using BGCS.Contracts.Common;

namespace BGCS.CSharp.Validation;

/// <summary>Reports a rejected managed ABI plan with its neutral native identity and original source position.</summary>
public sealed class CSharpPlanningException : NotSupportedException
{
    /// <summary>Captures an actionable ABI failure without retaining a frontend or source service.</summary>
    /// <param name="nativeIdentity">The exact native type, parameter or export that failed planning.</param>
    /// <param name="source">The detached original source position when known.</param>
    /// <param name="message">The rejected ABI or semantic capability and required remedy.</param>
    /// <param name="innerException">The underlying analysis failure when present.</param>
    public CSharpPlanningException(
        StableId nativeIdentity,
        SourceLocation? source,
        string message,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        this.nativeIdentity = nativeIdentity;
        diagnostic = new(BindingDiagnosticSeverity.Error, message, "BGCS-CSHARP-ABI-PLAN") { location = source };
    }

    /// <summary>Gets the exact neutral native identity that failed.</summary>
    public StableId nativeIdentity { get; }
    /// <summary>Gets the diagnostic suitable for CLI and C# host presentation.</summary>
    public BindingDiagnostic diagnostic { get; }
}
