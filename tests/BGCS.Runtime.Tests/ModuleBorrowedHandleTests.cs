using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class ModuleBorrowedHandleTests
{
    [Fact]
    public void ReferenceDisposalReleasesOnlyItsModuleLeaseAndNeverTheBorrowedStorage()
    {
        ProbeProvider provider = new();
        using NativeModuleSession session = new(provider);
        nint storage = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(storage, 42);
            ModuleBorrowedNativeHandle reference = new(storage, session.AcquireResource());
            Assert.Equal(42, reference.Invoke(Marshal.ReadInt32));
            reference.Dispose();
            reference.Dispose();
            Assert.Equal(42, Marshal.ReadInt32(storage));
            Assert.Equal(0, provider.releases);
            Assert.Throws<ObjectDisposedException>(() => reference.Invoke(address => address));
            session.Dispose();
            Assert.Equal(1, provider.releases);
        }
        finally { Marshal.FreeHGlobal(storage); }
    }

    [Fact]
    public void ClosingTheContextRejectsNewCallsButRetainsTheModuleUntilReferenceDisposal()
    {
        ProbeProvider provider = new();
        using NativeModuleSession session = new(provider);
        using ModuleBorrowedNativeHandle reference = new(42, session.AcquireResource());
        Assert.Equal((nint)42, reference.Invoke(address => address));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reference.Invoke(address => address));
        Assert.Equal(0, provider.releases);
        reference.Dispose();
        reference.Dispose();
        Assert.Equal(1, provider.releases);
    }

    [Fact]
    public async Task ContextAndReferenceDisposalCannotUnloadAnAlreadyStartedBorrowedCall()
    {
        ProbeProvider provider = new();
        using NativeModuleSession session = new(provider);
        using ModuleBorrowedNativeHandle reference = new(42, session.AcquireResource());
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim finish = new();
        Task<nint> operation = Task.Run(() => reference.Invoke(address =>
        {
            started.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The borrowed call was not released by its test.");
            Assert.Equal(0, Volatile.Read(ref provider.releases));
            return address;
        }));
        nint result = 0;
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            await Task.WhenAll(Task.Run(session.Dispose), Task.Run(reference.Dispose), Task.Run(reference.Dispose));
            Assert.Equal(0, provider.releases);
            Assert.Throws<ObjectDisposedException>(() => reference.Invoke(address => address));
        }
        finally { finish.Set(); result = await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal((nint)42, result);
        await session.DisposeAsync();
        Assert.Equal(1, provider.releases);
    }

    [Fact]
    public void ZeroReferenceImmediatelyReleasesTheAdoptedLeaseAndCannotBeInvoked()
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        ModuleBorrowedNativeHandle invalid = new(0, session.AcquireResource());
        Assert.True(invalid.IsInvalid);
        Assert.Throws<ObjectDisposedException>(() => invalid.Invoke(address => address));
        session.Dispose();
        Assert.Equal(1, provider.releases);
        invalid.Dispose();
        Assert.Equal(1, provider.releases);
    }

    [Fact]
    public void NullOperationDoesNotReleaseTheAdoptedLease()
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        ModuleBorrowedNativeHandle reference = new(42, session.AcquireResource());
        Assert.Throws<ArgumentNullException>(() => reference.Invoke<int>(null!));
        Assert.Equal((nint)42, reference.Invoke(address => address));
        session.Dispose();
        Assert.Equal(0, provider.releases);
        reference.Dispose();
        Assert.Equal(1, provider.releases);
        Assert.Throws<ArgumentNullException>(() => new ModuleBorrowedNativeHandle(42, null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedReferenceFinalizesItsLeaseWithAnOpenOrClosedContext(bool closeBeforeCollection)
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        WeakReference abandoned = Abandon(session);
        if (closeBeforeCollection) session.Dispose();
        Collect(abandoned);
        Assert.False(abandoned.IsAlive);
        Assert.Equal(closeBeforeCollection ? 1 : 0, provider.releases);
        await session.DisposeAsync();
        Assert.Equal(1, provider.releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModuleUnloadFailureIsCapturedDuringExplicitOrFinalizerLeaseRelease(bool finalize)
    {
        ProbeProvider provider = new() { failRelease = true };
        NativeModuleSession session = new(provider);
        if (finalize)
        {
            WeakReference abandoned = Abandon(session);
            session.Dispose();
            Collect(abandoned);
            Assert.False(abandoned.IsAlive);
        }
        else
        {
            ModuleBorrowedNativeHandle reference = new(42, session.AcquireResource());
            session.Dispose();
            reference.Dispose();
        }
        Assert.Equal(1, provider.releases);
        Assert.IsType<InvalidOperationException>(session.unloadFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(NativeModuleSession session) => new(new ModuleBorrowedNativeHandle(42, session.AcquireResource()));

    private static void Collect(WeakReference reference)
    {
        for (int attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
    }

    private sealed class ProbeProvider : INativeModuleProvider
    {
        internal int releases;
        internal bool failRelease;
        public bool TryGetExport(
            string name,
            out nint address
        ) { address = 0; return false; }
        public void Dispose()
        {
            Interlocked.Increment(ref releases);
            if (failRelease) throw new InvalidOperationException("The provider could not prove successful module unload.");
        }
    }
}
