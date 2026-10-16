using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class AsyncLifetimeTests
{
    [Fact]
    public async Task CancellationRequest_RetainsBufferUntilNativeAcknowledges()
    {
        ProbeResource resource = new();
        int requests = 0;
        NativeOperation<int> operation = new([resource], () => requests++);
        operation.Dispose();
        operation.Dispose();
        Assert.True(operation.isCancellationRequested);
        Assert.Equal(1, requests);
        Assert.False(operation.task.IsCompleted);
        Assert.Equal(0, resource.releases);
        Assert.True(operation.TrySetCanceled());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation.task);
        Assert.Equal(1, resource.releases);
    }

    [Fact]
    public async Task AsyncDisposal_AwaitsNativeCompletionWithoutAssumingCancellation()
    {
        ProbeResource resource = new();
        NativeOperation<int> operation = new([resource]);
        Task drain = operation.DisposeAsync().AsTask();
        Assert.False(drain.IsCompleted);
        Assert.Equal(0, resource.releases);
        Assert.True(operation.TrySetResult(42));
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, await operation.task);
        Assert.Equal(1, resource.releases);
    }

    [Fact]
    public async Task ClosingContext_AsyncOperationRetainsItsModuleUntilNativeCompletion()
    {
        ProbeResource provider = new();
        NativeModuleSession session = new(new StaticModuleProvider([], provider.Dispose));
        NativeOperation<int> operation = new([session.AcquireResource()]);
        session.Dispose();
        operation.Dispose();
        Assert.Equal(0, provider.releases);
        operation.TrySetResult(7);
        await session.DisposeAsync();
        Assert.Equal(1, provider.releases);
    }

    private sealed class ProbeResource : IDisposable
    {
        internal int releases;
        public void Dispose() => Interlocked.Increment(ref releases);
    }

    [Fact]
    public async Task SynchronousNativeCompletion_RetainsResourcesUntilCancelCallReturns()
    {
        ProbeResource resource = new();
        NativeOperation<int>? operation = null;
        operation = new([resource], () =>
        {
            Assert.True(operation!.TrySetResult(42));
            Assert.Equal(0, resource.releases);
            Assert.False(operation.task.IsCompleted);
        });
        operation.Dispose();
        Assert.Equal(42, await operation.task);
        Assert.Equal(1, resource.releases);
    }

    [Fact]
    public async Task ConcurrentCompletion_DoesNotUnloadDuringTheCancellationCall()
    {
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim leave = new();
        ProbeResource resource = new();
        NativeOperation<int> operation = new([resource], () =>
        {
            entered.Set();
            Assert.True(leave.Wait(TimeSpan.FromSeconds(5)));
        });
        Task cancel = Task.Run(operation.Dispose);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(operation.TrySetResult(7));
        Assert.Equal(0, resource.releases);
        leave.Set();
        await cancel.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(7, await operation.task);
        Assert.Equal(1, resource.releases);
    }
}
