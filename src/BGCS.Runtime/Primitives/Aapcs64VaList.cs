namespace BGCS.Runtime;

using System.Runtime.InteropServices;

/// <summary>
/// Blittable carrier for the <c>va_list</c> value defined by the Arm 64-bit
/// Procedure Call Standard (AAPCS64).
/// </summary>
/// <remarks>
/// This is an ABI transport type. Application code should normally obtain it from
/// native code rather than construct or inspect it directly.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct Aapcs64VaList
{
    /// <summary>Pointer to the next stacked argument.</summary>
    public nint Stack;

    /// <summary>End of the general-purpose register save area.</summary>
    public nint GeneralRegisterTop;

    /// <summary>End of the floating-point/SIMD register save area.</summary>
    public nint VectorRegisterTop;

    /// <summary>Current offset in the general-purpose register save area.</summary>
    public int GeneralRegisterOffset;

    /// <summary>Current offset in the floating-point/SIMD register save area.</summary>
    public int VectorRegisterOffset;
}
