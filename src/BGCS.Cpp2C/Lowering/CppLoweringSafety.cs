namespace BGCS.Cpp2C.Lowering;

/// <summary>Evidence level attached to extension-supplied code and conversion rules.</summary>
public enum CppLoweringSafety
{
    /// <summary>
    /// A built-in conversion with independently validated semantics.
    /// </summary>
    Verified,
    /// <summary>
    /// A conversion whose safety is asserted by the configuration or plugin author.
    /// </summary>
    UserAsserted,
    /// <summary>
    /// A conversion explicitly bypassing the normal safety requirements.
    /// </summary>
    Unsafe
}
