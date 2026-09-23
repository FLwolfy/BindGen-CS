using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>
/// Holds callback, buffer, and handle leases until a native asynchronous operation reaches one terminal state.
/// </summary>
public sealed class NativeAsyncOperation<TResult> : IDisposable
{
    private readonly object sync = new();
    private readonly List<IDisposable> lifetimes;
    private readonly TaskCompletionSource<TResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int terminal;

    /// <summary>Creates an operation owning the supplied lifetime resources.</summary>
    public NativeAsyncOperation(IEnumerable<IDisposable>? lifetimes = null)
    {
        this.lifetimes = lifetimes == null ? [] : new(lifetimes);
        if (this.lifetimes.Exists(resource => resource == null))
            throw new ArgumentException("A native async lifetime resource cannot be null.", nameof(lifetimes));
    }

    /// <summary>Task completed by the first terminal native signal.</summary>
    public Task<TResult> Task => completion.Task;

    /// <summary>Completes successfully and releases all retained resources.</summary>
    public bool TrySetResult(TResult result)
    {
        if (!TryBecomeTerminal())
            return false;
        Exception? releaseError = ReleaseLifetimes();
        return releaseError == null
            ? completion.TrySetResult(result)
            : completion.TrySetException(releaseError);
    }

    /// <summary>Completes with an exception and releases all retained resources.</summary>
    public bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!TryBecomeTerminal())
            return false;
        Exception? releaseError = ReleaseLifetimes();
        return completion.TrySetException(releaseError == null
            ? exception
            : new AggregateException("The native operation and lifetime cleanup both failed.", exception, releaseError));
    }

    /// <summary>Cancels and releases all retained resources.</summary>
    public bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        if (!TryBecomeTerminal())
            return false;
        Exception? releaseError = ReleaseLifetimes();
        return releaseError == null
            ? completion.TrySetCanceled(cancellationToken)
            : completion.TrySetException(releaseError);
    }

    /// <summary>Cancels an unfinished operation. Repeated disposal is harmless.</summary>
    public void Dispose() => TrySetCanceled();

    private bool TryBecomeTerminal() => Interlocked.CompareExchange(ref terminal, 1, 0) == 0;

    private Exception? ReleaseLifetimes()
    {
        List<Exception>? errors = null;
        lock (sync)
        {
            for (int index = lifetimes.Count - 1; index >= 0; index--)
            {
                try
                {
                    lifetimes[index].Dispose();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
            lifetimes.Clear();
        }
        return errors == null
            ? null
            : new AggregateException("One or more native async lifetime resources failed to release.", errors);
    }
}
