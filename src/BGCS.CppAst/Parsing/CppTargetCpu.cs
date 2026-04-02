using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Defines the target CPU used to compile a header file.
/// </summary>
public enum CppTargetCpu
{
    /// <summary>
    /// The x86 CPU family (32bit)
    /// </summary>
    X86,

    /// <summary>
    /// The X86_64 CPU family (64bit)
    /// </summary>
    X86_64,

    /// <summary>
    /// The ARM CPU family (32bit)
    /// </summary>
    ARM,

    /// <summary>
    /// The ARM 64 CPU family (64bit)
    /// </summary>
    ARM64
}
