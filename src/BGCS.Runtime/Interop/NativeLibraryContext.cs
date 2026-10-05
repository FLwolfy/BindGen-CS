using System;

namespace BGCS.Runtime;

/// <summary>
/// Owns a dynamically loaded native module and resolves symbols while that module remains alive.
/// </summary>
/// <remarks>
/// Lookup and disposal are serialized. Callers must stop invoking previously returned addresses before
/// disposing the context; a resolved address does not extend the module lifetime.
/// </remarks>
public sealed class NativeLibraryContext : INativeContext
{
    private readonly object m_gate = new();
    private nint m_library;
    private bool m_disposed;

    /// <summary>
    /// Takes ownership of an already loaded native module handle.
    /// </summary>
    /// <param name="library">
    /// A handle whose ownership is transferred to this context, or zero for an empty symbol source.
    /// The original owner must not release a transferred handle.
    /// </param>
    public NativeLibraryContext(nint library) => m_library = library;

    /// <summary>
    /// Loads a native module whose handle will be released by this context.
    /// </summary>
    /// <param name="libraryPath">
    /// The file path or platform-specific module name.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The module name is empty or whitespace.
    /// </exception>
    /// <exception cref="DllNotFoundException">
    /// The module cannot be loaded.
    /// </exception>
    public NativeLibraryContext(string libraryPath)
    {
        m_library = NativeLibrary.Load(libraryPath);
        if (m_library == 0)
            throw new DllNotFoundException($"The native module '{libraryPath}' could not be loaded.");
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">
    /// The context has released its module.
    /// </exception>
    public nint GetProcAddress(string procName)
        => TryGetProcAddress(procName, out nint address) ? address : 0;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">
    /// The context has released its module.
    /// </exception>
    public bool TryGetProcAddress(
        string procName,
        out nint address
    ) {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            return NativeLibrary.TryGetExport(m_library, procName, out address);
        }
    }

    /// <summary>
    /// Releases the owned module exactly once after concurrent lookup has finished.
    /// </summary>
    public void Dispose()
    {
        lock (m_gate)
        {
            if (m_disposed)
                return;
            NativeLibrary.Free(m_library);
            m_library = 0;
            m_disposed = true;
        }
    }

    /// <summary>
    /// Reports that dynamic library contexts do not provide extension discovery.
    /// </summary>
    /// <param name="extensionName">
    /// The extension token; dynamic modules expose symbols rather than extension capabilities.
    /// </param>
    /// <returns>
    /// Always false.
    /// </returns>
    public bool IsExtensionSupported(string extensionName) => false;
}
