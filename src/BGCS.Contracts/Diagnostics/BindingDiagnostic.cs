using System.Text.Json.Serialization;
using BGCS.Contracts.Common;

namespace BGCS.Contracts;

/// <summary>Represents one frontend, analysis, emission or validation diagnostic.</summary>
/// <param name="severity">Diagnostic severity.</param>
/// <param name="message">Actionable diagnostic text.</param>
/// <param name="code">Stable diagnostic code when available.</param>
public sealed record BindingDiagnostic(
    BindingDiagnosticSeverity severity,
    string message,
    string? code = null
)
{
    /// <summary>Gets the original header position when the producing stage can identify one.</summary>
    public SourceLocation? location { get; init; }
    /// <summary>Gets whether this diagnostic prevents successful generation or publication.</summary>
    [JsonIgnore]
    public bool isFailure => severity is BindingDiagnosticSeverity.Error or BindingDiagnosticSeverity.Critical;
}
