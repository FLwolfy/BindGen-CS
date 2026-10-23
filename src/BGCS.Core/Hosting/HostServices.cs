using System;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Core.Hosting;

/// <summary>Owns the explicitly supplied host facilities for a generation session.</summary>
public sealed class HostServices : IDisposable, IAsyncDisposable
{
    private int m_disposed;
    /// <summary>Creates an immutable session host binding.</summary>
    /// <param name="paths">The independently selected path implementation.</param>
    /// <param name="execution">Captured host process facts.</param>
    public HostServices(
        IHostPathService paths,
        HostExecutionFacts execution
    ) {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(execution);
        this.paths = paths;
        this.execution = execution;
    }
    /// <summary>Gets the session's path identity implementation.</summary>
    public IHostPathService paths { get; }
    /// <summary>Gets the host process facts independently of the generated target.</summary>
    public HostExecutionFacts execution { get; }
    /// <summary>Releases the owned path service once when it has a disposable lifetime.</summary>
    public void Dispose()
    {
        if (paths is IAsyncDisposable && paths is not IDisposable)
            throw new InvalidOperationException("This host service requires asynchronous disposal.");
        if (Interlocked.Exchange(ref m_disposed, 1) != 0) return;
        if (paths is IDisposable disposable) disposable.Dispose();
    }
    /// <summary>Releases asynchronous or synchronous owned path facilities once.</summary>
    /// <returns>The completion of owned service cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0) return;
        if (paths is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
        else if (paths is IDisposable disposable) disposable.Dispose();
    }
}
