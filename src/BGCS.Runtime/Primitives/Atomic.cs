using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>
/// Stores an unmanaged integer of at most 64 bits using atomic reads and compare-and-swap updates.
/// </summary>
/// <typeparam name="T">
/// The integer representation; arithmetic follows its unchecked overflow semantics.
/// </typeparam>
/// <remarks>
/// This value type must remain at a stable storage location while threads share it.
/// Copying the wrapper creates independent storage. Larger integers are rejected before access.
/// </remarks>
public struct Atomic<T> where T : unmanaged, IBinaryInteger<T>
{
    private long m_storage;

    /// <summary>
    /// Initializes independent atomic storage without reading beyond the supplied value.
    /// </summary>
    /// <param name="value">
    /// The initial integer.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// The integer representation occupies more than eight bytes.
    /// </exception>
    public Atomic(T value)
    {
        ValidateStorage();
        m_storage = long.CreateTruncating(value);
    }

    /// <summary>
    /// Gets or replaces the value atomically.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The integer representation occupies more than eight bytes, including on a default wrapper.
    /// </exception>
    public T value
    {
        get
        {
            ValidateStorage();
            return T.CreateTruncating(Interlocked.Read(ref m_storage));
        }
        set
        {
            ValidateStorage();
            Interlocked.Exchange(ref m_storage, long.CreateTruncating(value));
        }
    }

    /// <summary>
    /// Atomically adds one using the integer representation's unchecked arithmetic.
    /// </summary>
    /// <returns>
    /// The value installed by this operation, which may subsequently change on another thread.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// The integer representation exceeds eight bytes.
    /// </exception>
    public T Increment() => Add(T.One);

    /// <summary>
    /// Atomically subtracts one using the integer representation's unchecked arithmetic.
    /// </summary>
    /// <returns>
    /// The value installed by this operation, including wraparound at the minimum value.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// The integer representation exceeds eight bytes.
    /// </exception>
    public T Decrement() => Add(unchecked(-T.One));

    /// <summary>
    /// Adds an integer without losing concurrent updates or retaining noncanonical overflow bits.
    /// </summary>
    /// <param name="amount">
    /// The signed or unsigned amount to add.
    /// </param>
    /// <returns>
    /// The integer value installed by the successful compare-and-swap.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// The integer representation exceeds eight bytes.
    /// </exception>
    public T Add(T amount)
    {
        ValidateStorage();
        long previous = Interlocked.Read(ref m_storage);
        while (true)
        {
            T updated = unchecked(T.CreateTruncating(previous) + amount);
            long observed = Interlocked.CompareExchange(ref m_storage, long.CreateTruncating(updated), previous);
            if (observed == previous)
                return updated;
            previous = observed;
        }
    }

    /// <summary>
    /// Replaces the value only when its current integer representation equals the expected value.
    /// </summary>
    /// <param name="expected">
    /// The value required for replacement.
    /// </param>
    /// <param name="newValue">
    /// The replacement integer.
    /// </param>
    /// <returns>
    /// True when replacement succeeds; otherwise false with storage unchanged by this operation.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// The integer representation exceeds eight bytes.
    /// </exception>
    public bool CompareAndSwap(
        T expected,
        T newValue
    ) {
        ValidateStorage();
        long expectedStorage = long.CreateTruncating(expected);
        return Interlocked.CompareExchange(ref m_storage, long.CreateTruncating(newValue), expectedStorage) == expectedStorage;
    }

    private static void ValidateStorage()
    {
        if (Unsafe.SizeOf<T>() > sizeof(long))
            throw new NotSupportedException("Atomic integer storage supports representations of at most 64 bits.");
    }
}
