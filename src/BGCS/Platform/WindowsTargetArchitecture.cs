namespace BGCS;

/// <summary>
/// Identifies the Windows ABI architecture used to parse native headers and generate bindings.
/// </summary>
public enum WindowsTargetArchitecture
{
    /// <summary>
    /// 32-bit x86 Windows ABI.
    /// </summary>
    X86,

    /// <summary>
    /// 64-bit x86 Windows ABI.
    /// </summary>
    X64,

    /// <summary>
    /// 64-bit ARM Windows ABI.
    /// </summary>
    Arm64
}
