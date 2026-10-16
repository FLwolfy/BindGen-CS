using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Freezes an explicit export table for static linking or externally owned native modules.</summary>
public sealed class StaticModuleProvider : INativeModuleProvider
{
    private readonly FrozenDictionary<string, nint> m_exports;
    private readonly Action? m_release;
    private int m_disposed;

    /// <summary>Copies export addresses and optionally takes ownership of their release action.</summary>
    /// <param name="exports">Exact native names and their nonzero addresses.</param>
    /// <param name="release">A module release action, or null for borrowed static exports.</param>
    /// <exception cref="ArgumentException">An export name or address is invalid, or names collide.</exception>
    public StaticModuleProvider(
        IEnumerable<KeyValuePair<string, nint>> exports,
        Action? release = null
    ) {
        ArgumentNullException.ThrowIfNull(exports);
        Dictionary<string, nint> copy = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, nint> export in exports)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(export.Key);
            if (export.Value == 0)
                throw new ArgumentException("A static export address cannot be zero.", nameof(exports));
            copy.Add(export.Key, export.Value);
        }
        m_exports = copy.ToFrozenDictionary(StringComparer.Ordinal);
        m_release = release;
    }

    /// <inheritdoc/>
    public bool TryGetExport(
        string name,
        out nint address
    ) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
        return m_exports.TryGetValue(name, out address);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            m_release?.Invoke();
    }
}
