namespace BGCS;

/// <summary>
/// Defines one fixed managed signature for a native C variadic function.
/// </summary>
public sealed class VariadicFunctionVariant
{
    /// <summary>
    /// Gets or sets the suffix appended to generated method names.
    /// </summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets promoted managed ABI types appended after fixed native parameters.
    /// </summary>
    public List<string> ParameterTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets managed names for appended parameters. Missing names use <c>argN</c>.
    /// </summary>
    public List<string> ParameterNames { get; set; } = [];
}
