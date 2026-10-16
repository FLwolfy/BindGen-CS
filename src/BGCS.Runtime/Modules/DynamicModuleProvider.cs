using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BGCS.Runtime;

/// <summary>Owns one dynamically loaded native module independently of process-wide loader hooks.</summary>
public sealed class DynamicModuleProvider : INativeModuleProvider
{
    private readonly object m_gate = new();
    private readonly bool m_ownsHandle;
    private nint m_handle;

    /// <summary>Loads a native library using the platform's .NET native loader.</summary>
    /// <param name="libraryPath">The native library name or explicit path.</param>
    /// <param name="requestingAssembly">The consumer assembly whose native assets and assembly-load-context resolution apply, or null for direct platform loading.</param>
    /// <param name="searchPath">The optional consumer native search policy.</param>
    /// <exception cref="ArgumentException">The library path is empty.</exception>
    /// <exception cref="DllNotFoundException">The native library cannot be loaded.</exception>
    public DynamicModuleProvider(
        string libraryPath,
        Assembly? requestingAssembly = null,
        DllImportSearchPath? searchPath = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        m_handle = requestingAssembly is null ? System.Runtime.InteropServices.NativeLibrary.Load(libraryPath)
            : System.Runtime.InteropServices.NativeLibrary.Load(libraryPath, requestingAssembly, searchPath);
        m_ownsHandle = true;
    }

    private DynamicModuleProvider(
        nint handle,
        bool ownsHandle
    ) {
        if (handle == 0) throw new ArgumentException("A dynamic module provider requires a loaded nonzero native handle.", nameof(handle));
        m_handle = handle;
        m_ownsHandle = ownsHandle;
    }

    /// <summary>Creates an independent provider over an already loaded platform native-library handle.</summary>
    /// <param name="handle">A nonzero handle obtained from the platform's native library loader.</param>
    /// <param name="ownsHandle">True to transfer one loaded-handle reference after successful construction; false to borrow it.</param>
    /// <returns>A provider with its own symbol-lookup and disposal lifetime.</returns>
    /// <remarks>A borrowed handle's owner must keep it loaded until this provider and its session leases have finished.
    /// Lookup is serialized with provider disposal. Returned addresses do not retain the module; protect invocation with a session call lease or equivalent native lifetime synchronization.</remarks>
    /// <exception cref="ArgumentException">The handle is zero; ownership has not transferred.</exception>
    public static DynamicModuleProvider FromHandle(
        nint handle,
        bool ownsHandle
    ) => new(handle, ownsHandle);

    /// <inheritdoc/>
    public bool TryGetExport(
        string name,
        out nint address
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_handle == 0, this);
            return System.Runtime.InteropServices.NativeLibrary.TryGetExport(m_handle, name, out address) && address != 0;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (m_gate)
        {
            nint handle = m_handle;
            if (handle == 0) return;
            m_handle = 0;
            if (m_ownsHandle) System.Runtime.InteropServices.NativeLibrary.Free(handle);
        }
    }
}
