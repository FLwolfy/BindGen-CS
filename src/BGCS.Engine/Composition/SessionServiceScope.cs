using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BGCS.Composition;

internal sealed class SessionServiceScope : IAsyncDisposable
{
    private readonly List<object> m_owned = [];
    private readonly HashSet<object> m_released = new(ReferenceEqualityComparer.Instance);
    private bool m_closing;

    internal T Own<T>(T service) where T : class
    {
        ObjectDisposedException.ThrowIf(m_closing, this);
        if (!m_owned.Contains(service, ReferenceEqualityComparer.Instance)) m_owned.Add(service);
        return service;
    }

    internal async ValueTask ReleaseAsync(object service)
    {
        if (!m_released.Add(service)) return;
        if (service is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
        else if (service is IDisposable disposable) disposable.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        m_closing = true;
        List<Exception> failures = [];
        for (int index = m_owned.Count - 1; index >= 0; index--)
            try { await ReleaseAsync(m_owned[index]).ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        m_owned.Clear();
        if (failures.Count != 0) throw new AggregateException("Session generation services failed to release before publication.", failures);
    }
}
