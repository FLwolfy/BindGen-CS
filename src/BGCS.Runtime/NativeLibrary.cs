using System;

namespace BGCS.Runtime;

using System.Runtime.InteropServices;

/// <summary>
/// Cross-platform dynamic library loader for resolving native modules and exports.
/// </summary>
/// <remarks>
/// This helper uses platform-specific APIs (<c>LoadLibrary/GetProcAddress</c> or <c>dlopen/dlsym</c>) and returns
/// raw native handles/addresses consumed by runtime contexts and generated bindings.
/// </remarks>
public unsafe class NativeLibrary
{
        // Windows
        [DllImport("kernel32", EntryPoint = "LoadLibrary", SetLastError = true)]
        private static extern nint LoadLibraryNative(byte* lpFileName);

        [DllImport("kernel32", EntryPoint = "FreeLibrary", SetLastError = true)]
        private static extern bool FreeLibraryNative(nint hModule);

        [DllImport("kernel32", EntryPoint = "GetProcAddress", SetLastError = true)]
        private static extern nint GetProcAddressNative(nint hModule, byte* lpProcName);

        // Unix/Linux/Android
        [DllImport("libdl.so", EntryPoint = "dlopen")]
        private static extern nint DLOpenNative(byte* fileName, int flags);

        [DllImport("libdl.so", EntryPoint = "dlclose")]
        private static extern int DLCloseNative(nint handle);

        [DllImport("libdl.so", EntryPoint = "dlsym")]
        private static extern nint DLSymNative(nint handle, byte* name);

        // OSX
        [DllImport("libSystem.dylib", EntryPoint = "dlopen")]
        private static extern nint DLOpenNativeOSX(byte* fileName, int flags);

        [DllImport("libSystem.dylib", EntryPoint = "dlclose")]
        private static extern int DLCloseNativeOSX(nint handle);

        [DllImport("libSystem.dylib", EntryPoint = "dlsym")]
        private static extern nint DLSymNativeOSX(nint handle, byte* name);

        private const int RTLD_NOW = 2;

        /// <summary>
        /// Loads a dynamic library and returns its native handle.
        /// </summary>
        /// <param name="libraryPath">Path or platform-specific library name.</param>
        /// <returns>Native module handle, or <c>0</c> when loading fails.</returns>
        public static nint Load(string libraryPath)
        {
            byte* pLibraryPath = Utils.StringToUTF8Ptr(libraryPath);
            nint libraryHandle;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                libraryHandle = LoadLibraryNative(pLibraryPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                libraryHandle = DLOpenNative(pLibraryPath, RTLD_NOW);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                libraryHandle = DLOpenNativeOSX(pLibraryPath, RTLD_NOW);
            }
            else
            {
                libraryHandle = DLOpenNative(pLibraryPath, RTLD_NOW);
            }
            Utils.Free(pLibraryPath);
            return libraryHandle;
        }

        /// <summary>
        /// Unloads a previously loaded library handle.
        /// </summary>
        /// <param name="libraryHandle">Native module handle returned by <see cref="Load"/>.</param>
        /// <returns><see langword="true"/> when the unload operation succeeds; otherwise <see langword="false"/>.</returns>
        public static bool Free(nint libraryHandle)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return FreeLibraryNative(libraryHandle);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return DLCloseNative(libraryHandle) == 0;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return DLCloseNativeOSX(libraryHandle) == 0;
            }
            else
            {
                return DLCloseNative(libraryHandle) == 0;
            }
        }
        private static nint GetProcAddress(nint libraryHandle, byte* pFunctionName)
        {
            nint functionAddress;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                functionAddress = GetProcAddressNative(libraryHandle, pFunctionName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                functionAddress = DLSymNative(libraryHandle, pFunctionName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                functionAddress = DLSymNativeOSX(libraryHandle, pFunctionName);
            }
            else
            {
                functionAddress = DLSymNative(libraryHandle, pFunctionName);
            }

            return functionAddress;
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
            byte* pFunctionName = Utils.StringToUTF8Ptr(functionName);

            nint functionAddress = GetProcAddress(libraryHandle, pFunctionName);

            Utils.Free(pFunctionName);

            if (functionAddress == 0)
            {
                throw new EntryPointNotFoundException(functionName);
            }

            return functionAddress;
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
            byte* pFunctionName = Utils.StringToUTF8Ptr(functionName);

            functionAddress = GetProcAddress(libraryHandle, pFunctionName);

            Utils.Free(pFunctionName);

            if (functionAddress == 0)
            {
                return false;
            }

            return true;
        }
}
