using System;
using System.Threading;

namespace BGCS.Tool.Commands;

internal sealed class CommandCancellation : IDisposable
{
    private readonly CancellationTokenSource m_source = new();
    private readonly ConsoleCancelEventHandler m_handler;
    private readonly object m_gate = new();
    private bool m_disposed;

    internal CommandCancellation()
    {
        m_handler = (
            _,
            args
        ) => {
            args.Cancel = true;
            lock (m_gate)
                if (!m_disposed) m_source.Cancel();
        };
        Console.CancelKeyPress += m_handler;
    }

    internal CancellationToken token => m_source.Token;

    public void Dispose()
    {
        Console.CancelKeyPress -= m_handler;
        lock (m_gate)
        {
            if (m_disposed) return;
            m_disposed = true;
            m_source.Dispose();
        }
    }
}
