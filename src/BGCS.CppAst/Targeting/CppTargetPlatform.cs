namespace BGCS.CppAst.Targeting;

/// <summary>
/// Identifies an operating-system family targeted by native parsing and binding generation.
/// </summary>
public enum CppTargetPlatform
{
    /// <summary>
    /// Resolves to the operating system running the generator.
    /// </summary>
    Host,

    /// <summary>
    /// Microsoft Windows.
    /// </summary>
    Windows,

    /// <summary>
    /// GNU/Linux or another Linux distribution using the selected Linux ABI.
    /// </summary>
    Linux,

    /// <summary>
    /// Apple macOS.
    /// </summary>
    MacOS,

    /// <summary>
    /// Android.
    /// </summary>
    Android,

    /// <summary>
    /// Apple iOS.
    /// </summary>
    IOS,

    /// <summary>
    /// FreeBSD.
    /// </summary>
    FreeBSD
}
