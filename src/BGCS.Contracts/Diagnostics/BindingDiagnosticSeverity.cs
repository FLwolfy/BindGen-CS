namespace BGCS.Contracts;

/// <summary>Identifies the severity of a structured generation diagnostic.</summary>
public enum BindingDiagnosticSeverity
{
    /// <summary>Fine-grained generation tracing.</summary>
    Trace,
    /// <summary>Generation details useful during investigation.</summary>
    Debug,
    /// <summary>Normal progress or contextual information.</summary>
    Information,
    /// <summary>A recoverable condition requiring review.</summary>
    Warning,
    /// <summary>A condition that prevents a supported generation result.</summary>
    Error,
    /// <summary>A failure that prevents the generation operation from continuing safely.</summary>
    Critical
}
