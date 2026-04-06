namespace BGCS.Runtime
{
    using System;

    /// <summary>
    /// Abstraction over a native symbol source (shared library, process module, GL context, etc.).
    /// </summary>
    /// <remarks>
    /// Implementations are responsible for managing any native handle lifetime and returning function pointers
    /// that can be consumed by generated bindings.
    /// </remarks>
    public interface INativeContext : IDisposable
    {
        /// <summary>
        /// Resolves a native symbol and returns its address.
        /// </summary>
        /// <param name="procName">Exact export name to resolve.</param>
        /// <returns>The address of the symbol when found; otherwise <c>0</c>.</returns>
        nint GetProcAddress(string procName);

        /// <summary>
        /// Attempts to resolve a native symbol without throwing.
        /// </summary>
        /// <param name="procName">Exact export name to resolve.</param>
        /// <param name="procAddress">Resolved symbol address when found; otherwise <c>0</c>.</param>
        /// <returns><see langword="true"/> when the symbol exists; otherwise <see langword="false"/>.</returns>
        bool TryGetProcAddress(string procName, out nint procAddress);

        /// <summary>
        /// Indicates whether the context reports support for a named extension capability.
        /// </summary>
        /// <param name="extensionName">Extension token to test.</param>
        /// <returns><see langword="true"/> when supported; otherwise <see langword="false"/>.</returns>
        bool IsExtensionSupported(string extensionName);
    }
}
