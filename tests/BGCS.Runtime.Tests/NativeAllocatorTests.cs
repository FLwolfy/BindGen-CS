using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class NativeAllocatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(256)]
    public unsafe void NativeMemoryPair_RetainsBoundsAndRequestedAlignment(int alignment)
    {
        using NativeBuffer buffer = NativeAllocator.CreateNativeMemory().Allocate(9, (nuint)alignment);
        Assert.Equal((nuint)9, buffer.byteLength);
        Assert.Equal((nuint)alignment, buffer.alignment);
        Assert.Equal((nuint)0, buffer.allocatedByteLength % (nuint)alignment);
        Assert.Equal(45, buffer.Invoke((
            address,
            length
        ) => {
            Assert.Equal((nuint)0, (nuint)address % (nuint)alignment);
            int sum = 0;
            for (nuint index = 0; index < length; ++index) { ((byte*)address)[index] = (byte)(index + 1); sum += ((byte*)address)[index]; }
            return sum;
        }));
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.Invoke((
            address,
            _
        ) => address));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(12)]
    public void InvalidAlignment_IsRejectedBeforeNativeAllocation(int alignment)
    {
        int allocations = 0;
        NativeAllocator allocator = new("invalid", (
            _,
            _
        ) => { allocations++; return 64; }, (
            _,
            _,
            _
        ) => { }, NativeAllocatorThreadDomain.AnyThread);
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(1, (nuint)alignment));
        Assert.Equal(0, allocations);
    }

    [Fact]
    public void ZeroExtentAndTwoAllocators_KeepExactMatchingReleasePairs()
    {
        int firstFrees = 0;
        int secondFrees = 0;
        NativeAllocator first = new("first", (
            length,
            alignment
        ) => { Assert.Equal((nuint)64, length); Assert.Equal((nuint)64, alignment); return 64; },
            (
                address,
                length,
                alignment
            ) => { Assert.Equal((nint)64, address); Assert.Equal((nuint)64, length); Assert.Equal((nuint)64, alignment); firstFrees++; }, NativeAllocatorThreadDomain.AnyThread);
        NativeAllocator second = new("second", (
            _,
            _
        ) => 128, (
            _,
            _,
            _
        ) => secondFrees++, NativeAllocatorThreadDomain.AnyThread);
        using NativeBuffer a = first.Allocate(0, 64);
        using NativeBuffer b = second.Allocate(1, 64);
        Assert.Equal((nuint)0, a.byteLength);
        Assert.Equal("first", a.allocatorId);
        a.Dispose(); a.Dispose(); b.Dispose();
        Assert.Equal(1, firstFrees);
        Assert.Equal(1, secondFrees);
    }

    [Fact]
    public void Overflow_IsRejectedBeforeNativeAllocation()
    {
        int allocations = 0;
        NativeAllocator allocator = new("overflow", (
            _,
            _
        ) => { allocations++; return 64; }, (
            _,
            _,
            _
        ) => { }, NativeAllocatorThreadDomain.AnyThread);
        Assert.Throws<OverflowException>(() => allocator.Allocate(nuint.MaxValue, 64));
        Assert.Equal(0, allocations);
    }

    [Fact]
    public void ReturnedMisalignment_UsesMatchingFreeAndRetainsNoModule()
    {
        int frees = 0;
        int unloads = 0;
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        NativeAllocator allocator = new("misaligned", (
            _,
            _
        ) => 65, (
            address,
            length,
            alignment
        ) => { Assert.Equal((nint)65, address); Assert.Equal((nuint)64, length); Assert.Equal((nuint)64, alignment); frees++; }, NativeAllocatorThreadDomain.AnyThread, session);
        Assert.Throws<InvalidOperationException>(() => allocator.Allocate(9, 64));
        session.Dispose();
        Assert.Equal(1, frees);
        Assert.Equal(1, unloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocationFailure_ReleasesModuleLeaseWithoutInvokingFree(bool throwing)
    {
        int frees = 0;
        int unloads = 0;
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        NativeAllocator allocator = new("failed", (
            _,
            _
        ) => throwing ? throw new InvalidOperationException("allocate failed") : 0,
            (
                _,
                _,
                _
            ) => frees++, NativeAllocatorThreadDomain.AnyThread, session);
        if (throwing) Assert.Throws<InvalidOperationException>(() => allocator.Allocate(1, 8));
        else Assert.Throws<OutOfMemoryException>(() => allocator.Allocate(1, 8));
        session.Dispose();
        Assert.Equal(0, frees);
        Assert.Equal(1, unloads);
    }

    [Fact]
    public async Task ContextClosure_RejectsNewAllocationButAllowsMatchingCleanup()
    {
        int frees = 0;
        int unloads = 0;
        NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        NativeAllocator allocator = new("module-owned", (
            _,
            _
        ) => 64, (
            _,
            _,
            _
        ) => frees++, NativeAllocatorThreadDomain.AnyThread, session);
        NativeBuffer buffer = allocator.Allocate(8, 8);
        Task closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => allocator.Allocate(8, 8));
        Assert.Throws<ObjectDisposedException>(() => buffer.Invoke((
            address,
            _
        ) => address));
        await buffer.DisposeAsync();
        await closing;
        Assert.Equal(1, frees);
        Assert.Equal(1, unloads);
    }

    [Fact]
    public async Task ConcurrentDisposal_WaitsForInFlightBufferUse()
    {
        int frees = 0;
        int unloads = 0;
        NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        NativeBuffer buffer = new NativeAllocator("active", (
            _,
            _
        ) => 64, (
            _,
            _,
            _
        ) => Interlocked.Increment(ref frees), NativeAllocatorThreadDomain.AnyThread, session).Allocate(8, 8);
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim finish = new();
        Task<nint> call = Task.Run(() => buffer.Invoke((
            address,
            _
        ) => { started.Set(); Assert.True(finish.Wait(TimeSpan.FromSeconds(5))); Assert.Equal(0, Volatile.Read(ref frees)); return address; }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Task cleanup = buffer.DisposeAsync().AsTask();
        session.Dispose();
        Assert.False(cleanup.IsCompleted);
        Assert.Equal(0, unloads);
        finish.Set();
        Assert.Equal((nint)64, await call);
        await cleanup;
        Assert.Equal(1, frees);
        Assert.Equal(1, unloads);
    }

    [Fact]
    public void AffineCleanup_IsQueuedWithoutBlockingAndCompletesOnDeclaredThread()
    {
        Pump dispatcher = new();
        int frees = 0;
        NativeAllocator allocator = new("affine", (
            _,
            _
        ) => 64, (
            _,
            _,
            _
        ) => { Assert.True(dispatcher.CheckAccess()); frees++; }, NativeAllocatorThreadDomain.Dispatcher, dispatcher: dispatcher);
        NativeBuffer buffer = allocator.Allocate(8, 8);
        Exception? allocationFailure = null;
        Task? cleanup = null;
        Thread worker = new(() =>
        {
            try { allocator.Allocate(8, 8); } catch (Exception failure) { allocationFailure = failure; }
            cleanup = buffer.DisposeAsync().AsTask();
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(allocationFailure);
        Task actualCleanup = cleanup ?? throw new InvalidOperationException("The worker did not start buffer disposal.");
        Assert.False(actualCleanup.IsCompleted);
        Assert.Equal(0, frees);
        dispatcher.Drain();
        Assert.True(actualCleanup.IsCompletedSuccessfully);
        Assert.Equal(1, frees);
    }

    [Fact]
    public async Task AmbiguousReleaseFailure_IsObservableWithoutRetryOrModuleUnload()
    {
        int frees = 0;
        int unloads = 0;
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        NativeBuffer buffer = new NativeAllocator("failed-release", (
            _,
            _
        ) => 64,
            (
                _,
                _,
                _
            ) => { frees++; throw new InvalidOperationException("Unknown whether native free completed."); }, NativeAllocatorThreadDomain.AnyThread, session).Allocate(8, 8);
        session.Dispose();
        buffer.Dispose();
        Assert.IsType<InvalidOperationException>(buffer.releaseFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await buffer.DisposeAsync());
        buffer.Dispose();
        Assert.Equal(1, frees);
        Assert.Equal(0, unloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenDispatcher_ReportsFailureWithoutCallingFreeOrUnloading(bool rejectsPost)
    {
        int frees = 0;
        int unloads = 0;
        Pump dispatcher = new(executeInline: !rejectsPost, rejectPosts: rejectsPost);
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        NativeBuffer buffer = new NativeAllocator("broken-dispatch", (
            _,
            _
        ) => 64, (
            _,
            _,
            _
        ) => frees++,
            NativeAllocatorThreadDomain.Dispatcher, session, dispatcher).Allocate(8, 8);
        session.Dispose();
        Thread worker = new(buffer.Dispose);
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(buffer.releaseFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await buffer.DisposeAsync());
        Assert.Equal(0, frees);
        Assert.Equal(0, unloads);
    }

    [Fact]
    public void AbandonedAnyThreadBuffer_FinalizerFreesBeforeModuleUnload()
    {
        Counters counters = new();
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => Interlocked.Increment(ref counters.unloads)));
        NativeAllocator allocator = new("finalized", (
            _,
            _
        ) => 64, (
            _,
            _,
            _
        ) => { Assert.Equal(0, Volatile.Read(ref counters.unloads)); Interlocked.Increment(ref counters.frees); }, NativeAllocatorThreadDomain.AnyThread, session);
        WeakReference abandoned = Abandon(allocator);
        session.Dispose();
        for (int attempt = 0; attempt < 3 && abandoned.IsAlive; ++attempt)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(abandoned.IsAlive);
        Assert.Equal(1, counters.frees);
        Assert.Equal(1, counters.unloads);
        GC.KeepAlive(allocator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(NativeAllocator allocator) => new(allocator.Allocate(8, 8));

    [Theory]
    [InlineData(NativeAllocatorThreadDomain.Unknown, false)]
    [InlineData(NativeAllocatorThreadDomain.Dispatcher, false)]
    [InlineData(NativeAllocatorThreadDomain.AnyThread, true)]
    public void ThreadContract_MustExplicitlyMatchItsDispatcher(
        NativeAllocatorThreadDomain domain,
        bool dispatcher
    ) {
        Assert.Throws<ArgumentException>(() => new NativeAllocator("invalid-domain", (
            _,
            _
        ) => 64, (
            _,
            _,
            _
        ) => { }, domain,
            dispatcher: dispatcher ? new Pump() : null));
    }

    private sealed class Pump : INativeReleaseDispatcher
    {
        private readonly int m_thread = Environment.CurrentManagedThreadId;
        private readonly ConcurrentQueue<Action> m_pending = new();
        private readonly bool m_executeInline;
        private readonly bool m_rejectPosts;

        internal Pump(
            bool executeInline = false,
            bool rejectPosts = false
        ) {
            m_executeInline = executeInline;
            m_rejectPosts = rejectPosts;
        }

        public bool CheckAccess() => Environment.CurrentManagedThreadId == m_thread;
        public void Post(Action cleanup)
        {
            if (m_rejectPosts) throw new InvalidOperationException("Dispatcher is unavailable.");
            if (m_executeInline) cleanup();
            else m_pending.Enqueue(cleanup);
        }
        internal void Drain()
        {
            Assert.True(CheckAccess());
            while (m_pending.TryDequeue(out Action? cleanup)) cleanup();
        }
    }

    private sealed class Counters
    {
        internal int frees;
        internal int unloads;
    }
}
