using System;

namespace BGCS.Runtime;

/// <summary>Owns a native module or an explicitly supplied static export set.</summary>
/// <remarks>A session disposes its provider synchronously on the thread releasing the final lease,
/// which can be a finalizer thread. Providers must support that cleanup thread or require explicit
/// draining on their supported thread. This interface does not dispatch cleanup. Returning from
/// Dispose means the declared cleanup completed; throwing preserves the provider without retry.</remarks>
public interface INativeModuleProvider : IDisposable
{
    /// <summary>Resolves a callable address without transferring module ownership.</summary>
    /// <param name="name">The exact native export name.</param>
    /// <param name="address">The export address, or zero when unavailable.</param>
    /// <returns>True only when a nonzero address exists.</returns>
    bool TryGetExport(
        string name,
        out nint address
    );
}
