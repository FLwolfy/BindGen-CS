namespace BGCS.CppAst.Targeting;

/// <summary>
/// Identifies the processor architecture targeted by native parsing and binding generation.
/// </summary>
public enum CppTargetArchitecture
{
    /// <summary>
    /// Resolves to the process architecture running the generator.
    /// </summary>
    Host,

    /// <summary>
    /// 32-bit x86.
    /// </summary>
    X86,

    /// <summary>
    /// 64-bit x86.
    /// </summary>
    X64,

    /// <summary>
    /// 32-bit ARM.
    /// </summary>
    Arm,

    /// <summary>
    /// 64-bit ARM.
    /// </summary>
    Arm64
}
