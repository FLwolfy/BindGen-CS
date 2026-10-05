using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>
/// Retains native callback, buffer, and handle resources until the first terminal signal releases them in reverse order.
/// </summary>
/// <typeparam name="TResult">
/// The managed result supplied by successful native completion.
/// </typeparam>
public sealed class NativeAsyncOperation<TResult> : IDisposable
{
    private readonly object m_sync = new();
    private readonly List<IDisposable> m_lifetimes;
    private readonly TaskCompletionSource<TResult> m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int m_terminal;
    /// <summary>
    /// Takes ownership of a copied resource sequence for one native asynchronous operation.
    /// </summary>
    /// <param name="lifetimes">
    /// Resources to dispose in reverse enumeration order, or null for no retained resources. Elements must not be null.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The resource sequence contains a null element.
    /// </exception>
    public NativeAsyncOperation(IEnumerable<IDisposable>? lifetimes = null)
    {
        this.m_lifetimes = lifetimes == null ? [] : new(lifetimes);
        if (this.m_lifetimes.Exists(resource => resource == null))
            throw new ArgumentException("A native async lifetime resource cannot be null.", nameof(lifetimes));
    }

    /// <summary>Task completed by the first terminal native signal.</summary>
    public Task<TResult> task => this.m_completion.Task;

    /// <summary>
    /// Accepts the first completion signal and releases retained resources before settling the result task.
    /// </summary>
    /// <param name="result">
    /// The managed value supplied to the task when all lifetime cleanup succeeds.
    /// </param>
    /// <returns>
    /// True when this signal wins terminal ownership; cleanup failures fault the task instead of publishing the supplied value. False after another terminal signal.
    /// </returns>
    public bool TrySetResult(TResult result)
    {
        if (!TryBecomeTerminal())
            return false;
        Exception? releaseError = ReleaseLifetimes();
        return releaseError == null ? this.m_completion.TrySetResult(result) : this.m_completion.TrySetException(releaseError);
    }

    /// <summary>
    /// Accepts the first failure signal and combines native and lifetime-cleanup failures in the result task.
    /// </summary>
    /// <param name="exception">
    /// The non-null native-operation failure to publish.
    /// </param>
    /// <returns>
    /// True when this signal wins terminal ownership; false after another terminal signal.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The operation failure is null.
    /// </exception>
    public bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!TryBecomeTerminal())
            return false;
        Exception? releaseError = ReleaseLifetimes();
        return this.m_completion.TrySetException(releaseError == null ? exception : new AggregateException("The native operation and lifetime cleanup both failed.", exception, releaseError));
    }

    /// <summary>
    /// Accepts the first cancellation signal and releases all retained resources before settling the task.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token recorded on the canceled task when lifetime cleanup succeeds.
    /// </param>
    /// <returns>
    /// True when this signal wins terminal ownership. Cleanup failures fault the task; false means another signal already won.
    /// </returns>
    public bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        if (!TryBecomeTerminal())
            return false;
        Exception? releaseError = ReleaseLifetimes();
        return releaseError == null ? this.m_completion.TrySetCanceled(cancellationToken) : this.m_completion.TrySetException(releaseError);
    }

    /// <summary>Cancels an unfinished operation. Repeated disposal is harmless.</summary>
    public void Dispose() => TrySetCanceled();
    private bool TryBecomeTerminal() => Interlocked.CompareExchange(ref this.m_terminal, 1, 0) == 0;
    private Exception? ReleaseLifetimes()
    {
        List<Exception>? errors = null;
        lock (this.m_sync)
        {
            for (int index = this.m_lifetimes.Count - 1; index >= 0; index--)
            {
                try
                {
                    this.m_lifetimes[index].Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }

            this.m_lifetimes.Clear();
        }

        return errors == null ? null : new AggregateException("One or more native async lifetime resources failed to release.", errors);
    }
}
