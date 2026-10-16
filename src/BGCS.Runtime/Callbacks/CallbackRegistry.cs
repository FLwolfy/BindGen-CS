using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Roots typed callback state behind nonzero, non-reused tokens for generated static AOT thunks.</summary>
/// <typeparam name="TState">The generated registration state retained until native quiescence.</typeparam>
/// <remarks>A token is an opaque identity, never a dereferenceable GC handle. Removing it does not itself stop native execution.</remarks>
public static class CallbackRegistry<TState> where TState : class
{
    private static readonly ConcurrentDictionary<nint, TState> s_states = new();
    private static long s_lastToken;

    /// <summary>Retains one state until its registration is explicitly released after native stop-and-join.</summary>
    /// <param name="state">The managed state used by a generated static callback thunk.</param>
    /// <returns>An independently owned token registration.</returns>
    /// <exception cref="InvalidOperationException">The process has exhausted its non-reusable native-width token space.</exception>
    public static Registration Register(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        long limit = IntPtr.Size == 4 ? int.MaxValue : long.MaxValue;
        long previous;
        do
        {
            previous = Volatile.Read(ref s_lastToken);
            if (previous == limit) throw new InvalidOperationException("The callback token space is exhausted; released identities cannot be reused.");
        }
        while (Interlocked.CompareExchange(ref s_lastToken, previous + 1, previous) != previous);
        nint token = checked((nint)(previous + 1));
        if (!s_states.TryAdd(token, state)) throw new InvalidOperationException("A callback token was unexpectedly duplicated.");
        return new(token);
    }

    /// <summary>Resolves a live callback token without reading any native address or freed GC handle.</summary>
    /// <param name="token">The opaque user-data value passed back by native code.</param>
    /// <param name="state">The retained state when the token remains live.</param>
    /// <returns>False for zero, unknown or retired identities.</returns>
    public static bool TryGet(
        nint token,
        [NotNullWhen(true)] out TState? state
    ) => s_states.TryGetValue(token, out state);

    /// <summary>Owns one callback-state root; dispose only after the native unregister and join protocol is complete.</summary>
    public sealed class Registration : IDisposable
    {
        private nint m_token;
        internal Registration(nint token) { this.token = token; m_token = token; }
        /// <summary>Gets the original stable user-data identity, including after retirement.</summary>
        public nint token { get; }
        /// <summary>Retires the token exactly once without reusing it for another callback.</summary>
        /// <remarks>The caller must first stop and join native execution. Existing managed references remain ordinary safe references.</remarks>
        public void Dispose()
        {
            nint value = Interlocked.Exchange(ref m_token, 0);
            if (value != 0) s_states.TryRemove(value, out _);
        }
    }
}
