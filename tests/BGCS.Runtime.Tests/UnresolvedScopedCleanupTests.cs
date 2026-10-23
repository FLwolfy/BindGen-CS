using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class UnresolvedScopedCleanupTests
{
    [Fact]
    public async Task ReleasedForeignAndMissingCallsCannotRetainAnUnrelatedModule()
    {
        Provider provider = new(), other = new();
        NativeModuleSession session = new(provider), foreign = new(other);
        NativeModuleLease resource = session.AcquireResource();
        NativeCallLease call = resource.AcquireCall(), foreignCall = foreign.AcquireCall();
        Exception failure = new InvalidOperationException("unresolved native destructor");
        Assert.Throws<ArgumentNullException>(() => resource.RecordUnresolvedCleanup(null!, failure));
        Assert.Throws<ArgumentNullException>(() => resource.RecordUnresolvedCleanup(call, null!));
        Assert.Throws<InvalidOperationException>(() => resource.RecordUnresolvedCleanup(foreignCall, failure));
        call.Dispose();
        Assert.Throws<InvalidOperationException>(() => resource.RecordUnresolvedCleanup(call, failure));
        resource.Dispose();
        foreignCall.Dispose();
        await session.DisposeAsync();
        await foreign.DisposeAsync();
        Assert.Equal(1, provider.releases);
        Assert.Equal(1, other.releases);
    }

    [Fact]
    public async Task ClosingContextKeepsTheActiveCreatingCallAndFaultsUnloadAfterFailedDestruction()
    {
        Provider provider = new();
        NativeModuleSession session = new(provider);
        NativeModuleLease resource = session.AcquireResource();
        NativeCallLease call = resource.AcquireCall();
        Exception failure = new InvalidOperationException("native destruction failed");
        session.Dispose();
        resource.RecordUnresolvedCleanup(call, failure);
        call.Dispose();
        resource.Dispose();
        Exception observed = await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Same(failure, observed);
        Assert.Equal(0, provider.releases);
    }

    [Fact]
    public async Task AReleasedResourceCannotRecordFailureThroughAnotherRemainingCall()
    {
        Provider provider = new();
        NativeModuleSession session = new(provider);
        NativeModuleLease resource = session.AcquireResource();
        using (NativeCallLease call = resource.AcquireCall())
        {
            resource.Dispose();
            Assert.Throws<ObjectDisposedException>(() => resource.RecordUnresolvedCleanup(call, new Exception("too late")));
        }
        await session.DisposeAsync();
        Assert.Equal(1, provider.releases);
    }

    [Fact]
    public async Task ConcurrentRecordingAndLeaseDisposalNeverUnloadAfterAcceptedFailure()
    {
        for (int index = 0; index < 16; index++)
        {
            Provider provider = new();
            NativeModuleSession session = new(provider);
            NativeModuleLease resource = session.AcquireResource();
            NativeCallLease call = resource.AcquireCall();
            Exception failure = new InvalidOperationException("concurrent unresolved destruction");
            using ManualResetEventSlim start = new();
            bool accepted = false;
            Task record = Task.Run(() =>
            {
                start.Wait();
                try { resource.RecordUnresolvedCleanup(call, failure); accepted = true; }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
            Task release = Task.Run(() => { start.Wait(); session.Dispose(); call.Dispose(); resource.Dispose(); });
            start.Set();
            await Task.WhenAll(record, release).WaitAsync(TimeSpan.FromSeconds(5));
            if (accepted)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))));
                Assert.Equal(0, provider.releases);
            }
            else
            {
                await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(1, provider.releases);
            }
        }
    }

    private sealed class Provider : INativeModuleProvider
    {
        internal int releases;
        public bool TryGetExport(
            string name,
            out nint address
        ) { address = 0; return false; }
        public void Dispose() => Interlocked.Increment(ref releases);
    }
}
