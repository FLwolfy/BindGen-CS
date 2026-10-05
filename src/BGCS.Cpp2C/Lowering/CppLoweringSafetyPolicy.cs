namespace BGCS.Cpp2C.Lowering;

/// <summary>Controls which extension-supplied lowering evidence the generator accepts.</summary>
public enum CppLoweringSafetyPolicy
{
    /// <summary>
    /// Accept only verified conversion rules.
    /// </summary>
    VerifiedOnly,
    /// <summary>
    /// Also accept author-asserted conversion rules.
    /// </summary>
    AllowUserAsserted,
    /// <summary>
    /// Also accept explicitly unsafe rules and record each bypass.
    /// </summary>
    AllowUnsafe
}
