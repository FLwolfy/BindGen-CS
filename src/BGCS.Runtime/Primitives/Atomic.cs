using System.Threading;

namespace BGCS.Runtime;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Atomic value wrapper for unmanaged numeric-like types using interlocked primitives.
/// </summary>
/// <typeparam name="T">Unmanaged value type stored atomically.</typeparam>
public struct Atomic<T> where T : unmanaged
{
    /// <summary>
    /// Backing storage used for interlocked operations.
    /// </summary>
    /// <remarks>
    /// The value is represented as <see cref="ulong"/> and bit-cast to <typeparamref name="T"/>.
    /// </remarks>
    public ulong value;

    /// <summary>
    /// Initializes an atomic wrapper with an initial unmanaged value.
    /// </summary>
    /// <param name="value">Initial value.</param>
    public Atomic(T value)
    {
        this.value = Unsafe.As<T, ulong>(ref value);
    }

    /// <summary>
    /// Gets or sets the wrapped value using interlocked operations.
    /// </summary>
    public T Value
    {
        get { var n = Interlocked.Read(ref value); return Unsafe.As<ulong, T>(ref n); }
        set => Interlocked.Exchange(ref this.value, Unsafe.As<T, ulong>(ref value));
    }

    /// <summary>
    /// Atomically increments the current value by one.
    /// </summary>
    /// <returns>The incremented value.</returns>
    public T Increment()
    {
        ulong result = (ulong)Interlocked.Increment(ref Unsafe.As<ulong, long>(ref value));
        return Unsafe.As<ulong, T>(ref result);
    }

    /// <summary>
    /// Atomically decrements the current value by one.
    /// </summary>
    /// <returns>The decremented value.</returns>
    public T Decrement()
    {
        ulong result = (ulong)Interlocked.Decrement(ref Unsafe.As<ulong, long>(ref value));
        return Unsafe.As<ulong, T>(ref result);
    }

    /// <summary>
    /// Atomically adds <paramref name="amount"/> to the current value.
    /// </summary>
    /// <param name="amount">Amount to add.</param>
    /// <returns>The updated value after addition.</returns>
    public T Add(T amount)
    {
        ulong result = (ulong)Interlocked.Add(ref Unsafe.As<ulong, long>(ref value), Unsafe.As<T, long>(ref amount));
        return Unsafe.As<ulong, T>(ref result);
    }

    /// <summary>
    /// Performs an atomic compare-and-swap.
    /// </summary>
    /// <param name="expected">Expected current value.</param>
    /// <param name="newValue">Value to write when current value equals <paramref name="expected"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the swap succeeds; otherwise <see langword="false"/>.
    /// </returns>
    public bool CompareAndSwap(T expected, T newValue)
    {
        return Interlocked.CompareExchange(ref value, Unsafe.As<T, ulong>(ref newValue), Unsafe.As<T, ulong>(ref expected)) == Unsafe.As<T, ulong>(ref expected);
    }
}
