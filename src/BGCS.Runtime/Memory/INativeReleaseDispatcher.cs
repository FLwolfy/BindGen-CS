using System;

namespace BGCS.Runtime;

/// <summary>Schedules thread-affine native cleanup without blocking a finalizer or browser execution thread.</summary>
public interface INativeReleaseDispatcher
{
    /// <summary>Reports whether the calling thread may execute the allocator's callbacks.</summary>
    /// <returns>True only on an allowed native allocation and release thread.</returns>
    bool CheckAccess();

    /// <summary>Queues cleanup for reliable execution on an allowed thread without blocking for its completion.</summary>
    /// <param name="cleanup">An idempotently guarded cleanup action; the dispatcher must retain it until execution.</param>
    /// <remarks>The owner must keep the dispatcher alive and pumping until all allocation disposal operations complete.</remarks>
    void Post(Action cleanup);
}
