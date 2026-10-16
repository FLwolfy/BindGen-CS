using System;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class ExternalFunctionTableTests
{
    [Theory]
    [InlineData("identity")]
    [InlineData("extent")]
    [InlineData("zero")]
    [InlineData("duplicate")]
    public void InvalidTable_DoesNotTakeLifetimeOwnership(string fault)
    {
        Lease lease = new();
        string[] names = fault == "duplicate" ? ["first", "first"] : ["first", "second"];
        nint[] addresses = fault == "extent" ? [1] : fault == "zero" ? [1, 0] : [1, 2];
        Assert.Throws<ArgumentException>(() => new ExternalFunctionTableProvider("expected", fault == "identity" ? "other" : "expected", names, addresses, lease));
        Assert.Equal(0, lease.releaseCount);
    }

    [Fact]
    public async Task CopiedTable_RetainsExternalModuleUntilFinalCallLeaseReleases()
    {
        Lease lease = new();
        string[] names = ["first", "second"];
        nint[] addresses = [1, 2];
        ExternalFunctionTableProvider provider = new("complete-abi", "complete-abi", names, addresses, lease);
        names[0] = "changed";
        addresses[0] = 0;
        NativeModuleSession session = new(provider);
        Assert.Equal((nint)1, session.ResolveRequiredSymbols(["first", "second"])["first"]);
        NativeCallLease call = session.AcquireCall();
        Task closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.Equal(0, lease.releaseCount);
        call.Dispose();
        await closing;
        provider.Dispose();
        Assert.Equal(1, lease.releaseCount);
        Assert.Throws<ObjectDisposedException>(() => provider.TryGetExport("first", out _));
    }

    private sealed class Lease : IDisposable
    {
        public int releaseCount;
        public void Dispose() => releaseCount++;
    }
}
