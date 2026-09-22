namespace BGCS.CppAst.Targeting;

/// <summary>
/// Identifies the native ABI and C library family used by a target.
/// </summary>
public enum CppTargetAbi
{
    /// <summary>
    /// Selects the platform's conventional ABI.
    /// </summary>
    Default,

    /// <summary>
    /// Microsoft Visual C++ ABI.
    /// </summary>
    Msvc,

    /// <summary>
    /// GNU ABI with glibc.
    /// </summary>
    Gnu,

    /// <summary>
    /// GNU-compatible ABI with musl libc.
    /// </summary>
    Musl,

    /// <summary>
    /// Android ABI.
    /// </summary>
    Android,

    /// <summary>
    /// Apple Darwin ABI.
    /// </summary>
    Darwin
}
