namespace BGCS.Configuration.Mapping;

/// <summary>
/// Selects the unmanaged carrier emitted for native boolean values.
/// </summary>
public enum BoolType
{
    /// <summary>
    /// Uses the generated runtime's one-byte boolean wrapper.
    /// </summary>
    Bool8,

    /// <summary>
    /// Uses the generated runtime's four-byte boolean wrapper.
    /// </summary>
    Bool32,

    /// <summary>
    /// Uses an unmanaged byte without a boolean wrapper.
    /// </summary>
    Byte,

    /// <summary>
    /// Uses a signed four-byte integer without a boolean wrapper.
    /// </summary>
    Int32
}
