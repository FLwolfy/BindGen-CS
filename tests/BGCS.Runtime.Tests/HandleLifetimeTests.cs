using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class HandleLifetimeTests
{
    [Fact]
    public void ClosingContext_AllowsDestructionButRejectsNewObjectCalls()
    {
        int unloads = 0;
        int releases = 0;
        NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>(), () => unloads++));
        OwnedNativeHandle handle = new(42, session.AcquireResource(), _ => releases++, "fixture");
        BorrowedNativeHandle borrowed = handle.AsBorrowed();
        Assert.Equal((nint)42, borrowed.Invoke(address => address));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => borrowed.Invoke(address => address));
        Assert.Equal(0, unloads);
        handle.Dispose();
        handle.Dispose();
        Assert.Equal(1, releases);
        Assert.Equal(1, unloads);
        Assert.Throws<ObjectDisposedException>(() => borrowed.Invoke(address => address));
    }

    [Fact]
    public async Task ObjectDisposal_WaitsForAnAlreadyStartedOperation()
    {
        int releases = 0;
        using NativeModuleSession session = new(new StaticModuleProvider(new Dictionary<string, nint>()));
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim continueCall = new();
        OwnedNativeHandle handle = new(42, session.AcquireResource(), _ => Interlocked.Increment(ref releases), "fixture");
        Task operation = Task.Run(() => handle.Invoke(address =>
        {
            started.Set();
            Assert.True(continueCall.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, Volatile.Read(ref releases));
            return address;
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        handle.Dispose();
        Assert.Equal(0, releases);
        continueCall.Set();
        await operation;
        Assert.Equal(1, releases);
    }
}
