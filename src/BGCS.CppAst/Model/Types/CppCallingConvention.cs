using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Types;
using BGCS.CppAst.Model.Declarations;

/// <summary>
/// The calling function of a <see cref="CppFunction"/> or <see cref="CppFunctionType"/>
/// </summary>
public enum CppCallingConvention
{
    Default,
    C,
    X86StdCall,
    X86FastCall,
    X86ThisCall,
    X86Pascal,
    AAPCS,
    AAPCS_VFP,
    X86RegCall,
    IntelOclBicc,
    Win64,
    X86_64SysV,
    X86VectorCall,
    Swift,
    PreserveMost,
    PreserveAll,
    AArch64VectorCall,
    Invalid,
    Unexposed,
}
