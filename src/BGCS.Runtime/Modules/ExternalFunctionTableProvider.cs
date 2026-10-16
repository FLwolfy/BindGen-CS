using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Copies an explicitly identified external function table while owning its caller-supplied lifetime lease.</summary>
public sealed class ExternalFunctionTableProvider : INativeModuleProvider
{
    private readonly FrozenDictionary<string, nint> m_exports;
    private readonly IDisposable m_lifetime;
    private int m_disposed;

    /// <summary>Checks the full ABI identity and table extent before taking ownership of the supplied lifetime lease.</summary>
    /// <param name="expectedAbiIdentity">The generated complete ABI identity.</param>
    /// <param name="suppliedAbiIdentity">The external table producer's explicitly declared ABI identity.</param>
    /// <param name="symbols">The exact ordered native export names.</param>
    /// <param name="addresses">The matching nonzero callable addresses, copied before exposure.</param>
    /// <param name="lifetime">A lease guaranteeing every address remains valid until this provider disposes it.</param>
    /// <exception cref="ArgumentException">ABI identities differ, table lengths differ, or an export is invalid or duplicated.</exception>
    /// <remarks>The lease is transferred only after successful construction. An asserted ABI identity does not independently verify calling compatibility.</remarks>
    public ExternalFunctionTableProvider(
        string expectedAbiIdentity,
        string suppliedAbiIdentity,
        IReadOnlyList<string> symbols,
        ReadOnlySpan<nint> addresses,
        IDisposable lifetime
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAbiIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(suppliedAbiIdentity);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!string.Equals(expectedAbiIdentity, suppliedAbiIdentity, StringComparison.Ordinal))
            throw new ArgumentException("The external function table ABI identity differs from the generated binding contract.", nameof(suppliedAbiIdentity));
        if (symbols.Count != addresses.Length)
            throw new ArgumentException("The external function table extent differs from its complete symbol contract.", nameof(addresses));
        Dictionary<string, nint> exports = new(StringComparer.Ordinal);
        for (int index = 0; index < addresses.Length; index++)
        {
            string symbol = symbols[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
            if (addresses[index] == 0) throw new ArgumentException("An external function table contains a missing required address.", nameof(addresses));
            if (!exports.TryAdd(symbol, addresses[index])) throw new ArgumentException("An external function table contains a duplicate export name.", nameof(symbols));
        }
        m_exports = exports.ToFrozenDictionary(StringComparer.Ordinal);
        m_lifetime = lifetime;
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
        if (Interlocked.Exchange(ref m_disposed, 1) == 0) m_lifetime.Dispose();
    }
}
