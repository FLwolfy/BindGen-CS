using System.Collections.Generic;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Declarative callable-lowering recipe serialized in bridge configuration.</summary>
public sealed class CppCallableLoweringRecipe
{
    /// <summary>
    /// Stable registration name, unique within this extension category.
    /// </summary>
    public string name { get; set; } = string.Empty;
    /// <summary>
    /// Qualified or unqualified native callable-name glob.
    /// </summary>
    public string functionPattern { get; set; } = string.Empty;
    /// <summary>
    /// Selection priority; higher values are tried first and equal priorities use ordinal name order.
    /// </summary>
    public int priority { get; set; } = 100;
    /// <summary>
    /// Optional exported C symbol override; null uses the generated symbol.
    /// </summary>
    public string? exportName { get; set; }
    /// <summary>
    /// Whether the matched callable is intentionally omitted from the generated bridge.
    /// </summary>
    public bool exclude { get; set; }
    /// <summary>
    /// Optional expression wrapping the default invocation through the {invocation} placeholder.
    /// </summary>
    public string? invocationExpression { get; set; }
    /// <summary>
    /// Native headers required to compile this conversion or invocation.
    /// </summary>
    public List<string> requiredHeaders { get; set; } = [];
    /// <summary>
    /// Evidence level checked against the selected lowering safety policy.
    /// </summary>
    public CppLoweringSafety safety { get; set; } = CppLoweringSafety.UserAsserted;
}
