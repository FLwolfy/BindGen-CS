using System.Collections.Generic;

namespace BGCS.Cpp2C.Lowering;

/// <summary>
/// Callable-level lowering decision. <see cref = "invocationExpression"/> can wrap or replace the default
/// invocation using <c>{invocation}</c>; the generated expression must return the native callable result.
/// </summary>
/// <param name="loweringName">
/// Name of the extension that produced this decision.
/// </param>
/// <param name="exportName">
/// Nonempty C symbol to emit unless the callable is excluded.
/// </param>
public sealed record CppCallableLoweringPlan(
    string loweringName,
    string exportName
)
{
    /// <summary>
    /// Whether the matched callable is intentionally omitted from the generated bridge.
    /// </summary>
    public bool exclude { get; init; }
    /// <summary>
    /// Optional expression wrapping the default invocation through the {invocation} placeholder.
    /// </summary>
    public string? invocationExpression { get; init; }
    /// <summary>
    /// Native headers required to compile this conversion or invocation.
    /// </summary>
    public IReadOnlyList<string> requiredHeaders { get; init; } = [];
    /// <summary>
    /// Evidence level checked against the selected lowering safety policy.
    /// </summary>
    public CppLoweringSafety safety { get; init; } = CppLoweringSafety.Verified;
}
