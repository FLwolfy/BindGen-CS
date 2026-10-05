namespace BGCS.Intermediate;

/// <summary>
/// Describes one diagnostic condition and its general corrective action.
/// </summary>
/// <param name="code">Stable machine-readable diagnostic identifier.</param>
/// <param name="title">Short human-readable name of the condition.</param>
/// <param name="cause">Condition that produces the diagnostic.</param>
/// <param name="resolution">Corrective action, refined by the individual diagnostic when needed.</param>
public sealed record BindingDiagnosticDescriptor(
    string code,
    string title,
    string cause,
    string resolution
);
