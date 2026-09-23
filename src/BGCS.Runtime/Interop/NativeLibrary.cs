using System;

namespace BGCS.Runtime;

/// <summary>
/// Cross-platform dynamic library loader for resolving native modules and exports.
/// </summary>
/// <remarks>
/// The implementation delegates platform ABI details to the .NET native-library loader and returns raw handles and
/// addresses consumed by runtime contexts and generated bindings.
/// </remarks>
public class NativeLibrary
{
    /// <summary>
    /// Loads a dynamic library and returns its native handle.
    /// </summary>
    /// <param name="libraryPath">Path or platform-specific library name.</param>
    /// <returns>Native module handle, or <c>0</c> when loading fails.</returns>
    public static nint Load(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        return global::System.Runtime.InteropServices.NativeLibrary.TryLoad(libraryPath, out nint handle)
            ? handle
            : 0;
    }

    /// <summary>
    /// Unloads a previously loaded library handle.
    /// </summary>
    /// <param name="libraryHandle">Native module handle returned by <see cref="Load"/>.</param>
    /// <returns><see langword="true"/> when the unload operation succeeds; otherwise <see langword="false"/>.</returns>
    public static bool Free(nint libraryHandle)
    {
        if (libraryHandle == 0)
        {
            return false;
        }

        global::System.Runtime.InteropServices.NativeLibrary.Free(libraryHandle);
        return true;
    }

    /// <summary>
    /// Resolves an exported symbol and throws when not found.
    /// </summary>
    /// <param name="libraryHandle">Native module handle.</param>
    /// <param name="functionName">Export name to resolve.</param>
    /// <returns>Address of the resolved symbol.</returns>
    /// <exception cref="EntryPointNotFoundException">
    /// Thrown when <paramref name="functionName"/> does not exist in <paramref name="libraryHandle"/>.
    /// </exception>
    public static nint GetExport(nint libraryHandle, string functionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        return global::System.Runtime.InteropServices.NativeLibrary.GetExport(libraryHandle, functionName);
    }

    /// <summary>
    /// Attempts to resolve an exported symbol without throwing.
    /// </summary>
    /// <param name="libraryHandle">Native module handle.</param>
    /// <param name="functionName">Export name to resolve.</param>
    /// <param name="functionAddress">Resolved function address, or <c>0</c> when not found.</param>
    /// <returns><see langword="true"/> when the export exists; otherwise <see langword="false"/>.</returns>
    public static bool TryGetExport(nint libraryHandle, string functionName, out nint functionAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        if (libraryHandle == 0)
        {
            functionAddress = 0;
            return false;
        }

        return global::System.Runtime.InteropServices.NativeLibrary.TryGetExport(
            libraryHandle,
            functionName,
            out functionAddress);
    }
}
