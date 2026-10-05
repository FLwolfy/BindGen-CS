// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model.Types;

using BGCS.CppAst.Model.Declarations;

/// <summary>
/// The calling function of a <see cref = "CppFunction"/> or <see cref = "CppFunctionType"/>
/// </summary>
public enum CppCallingConvention
{
    /// <summary>
    /// The target default calling convention.
    /// </summary>
    Default,
    /// <summary>
    /// The C calling convention for the selected target.
    /// </summary>
    C,
    /// <summary>
    /// The x86 stdcall convention.
    /// </summary>
    X86StdCall,
    /// <summary>
    /// The x86 fastcall convention.
    /// </summary>
    X86FastCall,
    /// <summary>
    /// The x86 instance-method thiscall convention.
    /// </summary>
    X86ThisCall,
    /// <summary>
    /// The x86 Pascal convention.
    /// </summary>
    X86Pascal,
    /// <summary>
    /// The Arm Architecture Procedure Call Standard.
    /// </summary>
    AAPCS,
    /// <summary>
    /// The Arm procedure-call standard using floating-point registers.
    /// </summary>
    AAPCS_VFP,
    /// <summary>
    /// The x86 register-call convention.
    /// </summary>
    X86RegCall,
    /// <summary>
    /// The Intel OpenCL built-in function convention.
    /// </summary>
    IntelOclBicc,
    /// <summary>
    /// The Windows x64 procedure-call convention.
    /// </summary>
    Win64,
    /// <summary>
    /// The x86-64 System V convention.
    /// </summary>
    X86_64SysV,
    /// <summary>
    /// The x86 vectorcall convention.
    /// </summary>
    X86VectorCall,
    /// <summary>
    /// The Swift procedure-call convention.
    /// </summary>
    Swift,
    /// <summary>
    /// The Clang convention preserving most caller registers.
    /// </summary>
    PreserveMost,
    /// <summary>
    /// The Clang convention preserving all caller registers.
    /// </summary>
    PreserveAll,
    /// <summary>
    /// The AArch64 vector-call convention.
    /// </summary>
    AArch64VectorCall,
    /// <summary>
    /// No valid calling convention could be determined.
    /// </summary>
    Invalid,
    /// <summary>
    /// The parser did not expose the native calling convention.
    /// </summary>
    Unexposed,
}
