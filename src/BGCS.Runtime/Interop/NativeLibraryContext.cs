namespace BGCS.Runtime
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Default <see cref="INativeContext"/> backed by a dynamically loaded native library.
    /// </summary>
    public class NativeLibraryContext : INativeContext
    {
        private nint library;

        /// <summary>
        /// Wraps an already loaded native library handle.
        /// </summary>
        /// <param name="library">Native module handle.</param>
        public NativeLibraryContext(nint library)
        {
            this.library = library;
        }

        /// <summary>
        /// Loads a native library and creates a context for symbol resolution.
        /// </summary>
        /// <param name="libraryPath">Path or logical name of the library.</param>
        public NativeLibraryContext(string libraryPath)
        {
            library = NativeLibrary.Load(libraryPath);
        }

        /// <summary>
        /// Resolves an exported symbol to its native address.
        /// </summary>
        /// <param name="procName">Export name.</param>
        /// <returns>Export address when found; otherwise <c>0</c>.</returns>
        public nint GetProcAddress(string procName)
        {
            if (!NativeLibrary.TryGetExport(library, procName, out var address))
            {
                return 0;
            }

            return address;
        }

        /// <summary>
        /// Attempts to resolve an export without throwing.
        /// </summary>
        /// <param name="procName">Export name.</param>
        /// <param name="address">Resolved address, or <c>0</c> when not found.</param>
        /// <returns><see langword="true"/> when the symbol exists; otherwise <see langword="false"/>.</returns>
        public bool TryGetProcAddress(string procName, out nint address)
        {
            return NativeLibrary.TryGetExport(library, procName, out address);
        }

        /// <summary>
        /// Releases the loaded library handle if this context still owns one.
        /// </summary>
        public void Dispose()
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
                library = 0;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Indicates whether the specified extension is supported by this context.
        /// </summary>
        /// <param name="extensionName">Extension token to test.</param>
        /// <returns>
        /// Always returns <see langword="false"/> for <see cref="NativeLibraryContext"/>.
        /// </returns>
        public bool IsExtensionSupported(string extensionName)
        {
            return false;
        }
    }
}
