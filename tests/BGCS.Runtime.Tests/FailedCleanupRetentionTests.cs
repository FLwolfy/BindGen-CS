using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class FailedCleanupRetentionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AmbiguousResourceCleanup_RetainsFinalizableProviderWithoutRootingFailedHandle(
        bool ownedHandle,
        bool disposeResource,
        bool closeContext
    ) {
        Counters counters = new();
        Abandoned abandoned = CreateResource(counters, ownedHandle, disposeResource, closeContext, failRelease: true);
        Collect();
        Assert.False(abandoned.resource!.IsAlive);
        Assert.True(abandoned.session.IsAlive);
        Assert.True(abandoned.provider.IsAlive);
        Assert.Equal(1, counters.releaseAttempts);
        Assert.Equal(0, counters.providerDisposeAttempts);
        Assert.Equal(0, counters.moduleUnloads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuccessfulResourceFinalization_ReleasesModuleAndAllSessionRoots(
        bool ownedHandle,
        bool closeContext
    ) {
        Counters counters = new();
        Abandoned abandoned = CreateResource(counters, ownedHandle, disposeResource: false, closeContext, failRelease: false);
        Collect();
        Assert.False(abandoned.resource!.IsAlive);
        Assert.False(abandoned.session.IsAlive);
        Assert.False(abandoned.provider.IsAlive);
        Assert.Equal(1, counters.releaseAttempts);
        Assert.Equal(1, counters.providerDisposeAttempts);
        Assert.Equal(1, counters.moduleUnloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProviderUnloadFailure_DoesNotFallBackToSafeHandleFinalizationOrRetry(bool closeContext)
    {
        Counters counters = new();
        Abandoned abandoned = CreateSession(counters, closeContext, failUnload: true);
        Collect();
        Assert.True(abandoned.session.IsAlive);
        Assert.True(abandoned.provider.IsAlive);
        Assert.Equal(1, counters.providerDisposeAttempts);
        Assert.Equal(0, counters.moduleUnloads);
        NativeModuleSession session = (NativeModuleSession)abandoned.session.Target!;
        Assert.IsType<InvalidOperationException>(session.unloadFailure);
        Assert.True(session.DisposeAsync().AsTask().IsFaulted);
        session.Dispose();
        Assert.Equal(1, counters.providerDisposeAttempts);
    }

    [Fact]
    public void AbandonedContextWithoutResources_StillFinalizesAndReleasesItsReservedRoot()
    {
        Counters counters = new();
        Abandoned abandoned = CreateSession(counters, closeContext: false, failUnload: false);
        Collect();
        Assert.False(abandoned.session.IsAlive);
        Assert.False(abandoned.provider.IsAlive);
        Assert.Equal(1, counters.providerDisposeAttempts);
        Assert.Equal(1, counters.moduleUnloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownTerminalResourceFailure_FaultsPendingClosureWithoutUnloadingOrRetry(bool ownedHandle)
    {
        Counters counters = new();
        NativeModuleSession session = new(new FinalizableProvider(counters));
        void Release()
        { Interlocked.Increment(ref counters.releaseAttempts); throw new InvalidOperationException("Terminal native resource cleanup failure."); }
        SafeHandle resource = ownedHandle
            ? new OwnedNativeHandle(64, session.AcquireResource(), _ => Release(), "test-domain")
            : new NativeAllocator("test-domain", (
                _,
                _
            ) => 64, (
                _,
                _,
                _
            ) => Release(), NativeAllocatorThreadDomain.AnyThread, session).Allocate(8, 8);
        Task closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        resource.Dispose();
        Assert.True(closing.IsFaulted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await closing.WaitAsync(TimeSpan.FromSeconds(5)));
        resource.Dispose();
        Assert.Equal(1, counters.releaseAttempts);
        Assert.Equal(0, counters.providerDisposeAttempts);
        Assert.Equal(0, counters.moduleUnloads);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Abandoned CreateResource(
        Counters counters,
        bool ownedHandle,
        bool disposeResource,
        bool closeContext,
        bool failRelease
    ) {
        FinalizableProvider provider = new(counters);
        NativeModuleSession session = new(provider);
        void Release()
        {
            Interlocked.Increment(ref counters.releaseAttempts);
            if (failRelease) throw new InvalidOperationException("The native resource cleanup outcome is ambiguous.");
        }
        SafeHandle resource = ownedHandle
            ? new OwnedNativeHandle(64, session.AcquireResource(), _ => Release(), "test-domain")
            : new NativeAllocator("test-domain", (
                _,
                _
            ) => 64, (
                _,
                _,
                _
            ) => Release(), NativeAllocatorThreadDomain.AnyThread, session).Allocate(8, 8);
        if (closeContext) session.Dispose();
        if (disposeResource) resource.Dispose();
        return new(new(session, trackResurrection: true), new(provider, trackResurrection: true), new(resource, trackResurrection: true));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Abandoned CreateSession(
        Counters counters,
        bool closeContext,
        bool failUnload
    ) {
        FinalizableProvider provider = new(counters, failUnload);
        NativeModuleSession session = new(provider);
        if (closeContext) session.Dispose();
        return new(new(session, trackResurrection: true), new(provider, trackResurrection: true), null);
    }

    private static void Collect()
    {
        for (int attempt = 0; attempt < 6; ++attempt)
        { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    }

    private sealed record Abandoned(
        WeakReference session,
        WeakReference provider,
        WeakReference? resource
    );

    private sealed class Counters
    {
        internal int releaseAttempts;
        internal int providerDisposeAttempts;
        internal int moduleUnloads;
    }

    private sealed class FinalizableProvider : INativeModuleProvider
    {
        private readonly ModuleHandle m_handle;
        private readonly Counters m_counters;
        private readonly bool m_failUnload;
        internal FinalizableProvider(
            Counters counters,
            bool failUnload = false
        ) { m_counters = counters; m_failUnload = failUnload; m_handle = new(counters); }
        public bool TryGetExport(
            string name,
            out nint address
        ) { address = 0; return false; }
        public void Dispose()
        {
            Interlocked.Increment(ref m_counters.providerDisposeAttempts);
            if (m_failUnload) throw new InvalidOperationException("The native module unload outcome is ambiguous.");
            m_handle.Dispose();
        }
    }

    private sealed class ModuleHandle : SafeHandle
    {
        private readonly Counters m_counters;
        internal ModuleHandle(Counters counters) : base(0, ownsHandle: true) { m_counters = counters; SetHandle(1); }
        public override bool IsInvalid => handle == 0;
        // A real SafeHandle finalizer records the unload boundary; the synthetic address is never dereferenced.
        protected override bool ReleaseHandle() { Interlocked.Increment(ref m_counters.moduleUnloads); return true; }
    }
}
