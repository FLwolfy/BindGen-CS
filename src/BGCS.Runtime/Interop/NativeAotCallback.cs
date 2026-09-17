using System;

namespace BGCS.Runtime;

/// <summary>
/// Converts statically generated <see cref="System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"/> thunks to native callback addresses without delegate marshalling.
/// </summary>
public static unsafe class NativeAotCallback
{
    /// <summary>
    /// Returns the native address of an unmanaged function pointer.
    /// </summary>
    /// <param name="callback">Function pointer to a static unmanaged-callable method.</param>
    /// <returns>The pointer value, or zero for a null pointer.</returns>
    public static nint GetFunctionPointer(void* callback) => (nint)callback;
}
