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
public sealed class NativeOperation<TResult> : IDisposable, IAsyncDisposable
{
    private readonly object m_sync = new();
    private readonly List<IDisposable> m_lifetimes;
    private readonly TaskCompletionSource<TResult> m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int m_terminal;
    private int m_cancelRequested;
    private readonly Action? m_requestCancellation;
    private int m_cancellationCalls;
    private Action? m_pendingCompletion;
    /// <summary>
    /// Takes ownership of a copied resource sequence for one native asynchronous operation.
    /// </summary>
    /// <param name="lifetimes">
    /// Resources to dispose in reverse enumeration order, or null for no retained resources. Elements must not be null.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The resource sequence contains a null element.
    /// </exception>
    /// <param name="requestCancellation">Optional native cancellation request; it must not claim completion before native resource access has stopped.</param>
    public NativeOperation(
        IEnumerable<IDisposable>? lifetimes = null,
        Action? requestCancellation = null
    ) {
        this.m_lifetimes = lifetimes == null ? [] : new(lifetimes);
        if (this.m_lifetimes.Exists(resource => resource == null))
            throw new ArgumentException("A native async lifetime resource cannot be null.", nameof(lifetimes));
        m_requestCancellation = requestCancellation;
    }

    /// <summary>Task completed by the first terminal native signal.</summary>
    public Task<TResult> task => this.m_completion.Task;

    /// <summary>Gets whether cancellation has been requested without assuming native completion.</summary>
    public bool isCancellationRequested => Volatile.Read(ref m_cancelRequested) != 0;

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
        CompleteAfterCancellationCall(() =>
        {
            Exception? releaseError = ReleaseLifetimes();
            if (releaseError == null)
                m_completion.TrySetResult(result);
            else
                m_completion.TrySetException(releaseError);
        });
        return true;
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
        CompleteAfterCancellationCall(() =>
        {
            Exception? releaseError = ReleaseLifetimes();
            m_completion.TrySetException(releaseError == null ? exception : new AggregateException("The native operation and lifetime cleanup both failed.", exception, releaseError));
        });
        return true;
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
        CompleteAfterCancellationCall(() =>
        {
            Exception? releaseError = ReleaseLifetimes();
            if (releaseError == null)
                m_completion.TrySetCanceled(cancellationToken);
            else
                m_completion.TrySetException(releaseError);
        });
        return true;
    }

    /// <summary>Requests cancellation without releasing resources still reachable from native code.</summary>
    /// <remarks>The native completion callback must still call a terminal method after native access has stopped.</remarks>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_terminal != 0 || m_cancelRequested != 0)
                return;
            Volatile.Write(ref m_cancelRequested, 1);
            m_cancellationCalls++;
        }
        try
        {
            m_requestCancellation?.Invoke();
        }
        catch
        {
            lock (m_sync)
                if (m_terminal == 0)
                    Volatile.Write(ref m_cancelRequested, 0);
            throw;
        }
        finally
        {
            Action? complete;
            lock (m_sync)
            {
                m_cancellationCalls--;
                complete = m_cancellationCalls == 0 ? m_pendingCompletion : null;
                if (complete is not null)
                    m_pendingCompletion = null;
            }
            complete?.Invoke();
        }
    }

    /// <summary>Requests cancellation and awaits the terminal native acknowledgement and cleanup.</summary>
    /// <returns>The native operation task, including its result, failure or acknowledged cancellation.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(m_completion.Task);
    }
    private bool TryBecomeTerminal() => Interlocked.CompareExchange(ref this.m_terminal, 1, 0) == 0;

    private void CompleteAfterCancellationCall(Action complete)
    {
        lock (m_sync)
        {
            if (m_cancellationCalls != 0)
            {
                m_pendingCompletion = complete;
                return;
            }
        }
        complete();
    }
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
