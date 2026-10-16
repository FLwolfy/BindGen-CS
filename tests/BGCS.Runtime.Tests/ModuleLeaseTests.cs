using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class ModuleLeaseTests
{
    [Fact]
    public void ClosingContext_RetainsAlreadyStartedCalls()
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        NativeCallLease call = session.AcquireCall();
        session.Dispose();
        Assert.Equal(0, provider.releaseCount);
        Assert.Throws<ObjectDisposedException>(() => session.AcquireCall());
        call.Dispose();
        call.Dispose();
        Assert.Equal(1, provider.releaseCount);
    }

    [Fact]
    public void OwnedResource_CanCleanUpAfterContextCloses()
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        NativeModuleLease resource = session.AcquireResource();
        session.Dispose();
        using (NativeCallLease cleanup = resource.AcquireCleanupCall())
        {
            resource.Dispose();
            Assert.Equal(0, provider.releaseCount);
        }
        Assert.Equal(1, provider.releaseCount);
        Assert.Throws<ObjectDisposedException>(() => resource.AcquireCleanupCall());
    }

    [Fact]
    public async Task AsyncClosure_WaitsForTheFinalResource()
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        NativeModuleLease resource = session.AcquireResource();
        Task close = session.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        resource.Dispose();
        await close;
        Assert.Equal(1, provider.releaseCount);
    }

    [Fact]
    public async Task ConcurrentDisposal_UnloadsOnce()
    {
        ProbeProvider provider = new();
        NativeModuleSession session = new(provider);
        NativeCallLease call = session.AcquireCall();
        await Task.WhenAll(Task.Run(session.Dispose), Task.Run(session.Dispose),
            Task.Run(call.Dispose), Task.Run(call.Dispose));
        await session.DisposeAsync();
        Assert.Equal(1, provider.releaseCount);
    }

    [Fact]
    public void MissingRequiredExport_DoesNotExposePartialTable()
    {
        ProbeProvider provider = new();
        using NativeModuleSession session = new(provider);
        Assert.Throws<EntryPointNotFoundException>(() => session.ResolveRequiredSymbols(["present", "missing"]));
        IReadOnlyDictionary<string, nint> table = session.ResolveRequiredSymbols(["present"]);
        Assert.Equal((nint)1, table["present"]);
    }

    [Fact]
    public async Task ProviderReleaseFailure_IsObservableWithoutEscapingLeaseDisposal()
    {
        ProbeProvider provider = new() { failRelease = true };
        NativeModuleSession session = new(provider);
        session.Dispose();
        Assert.IsType<InvalidOperationException>(session.unloadFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
    }

    private sealed class ProbeProvider : INativeModuleProvider
    {
        public int releaseCount;
        public bool failRelease;

        public bool TryGetExport(
            string name,
            out nint address
        ) {
            address = name == "present" ? 1 : 0;
            return address != 0;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref releaseCount);
            if (failRelease)
                throw new InvalidOperationException("Native provider release failed.");
        }
    }
}
