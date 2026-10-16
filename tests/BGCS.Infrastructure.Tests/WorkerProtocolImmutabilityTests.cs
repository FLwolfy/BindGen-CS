using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Caching;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class WorkerProtocolImmutabilityTests
{
    [Fact]
    public void FreshProtocolOptionsAreFrozenBeforeAnySerializationCanOccur()
    {
        AssemblyLoadContext isolated = new("bgcs-protocol-options-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            Assembly assembly = isolated.LoadFromAssemblyPath(typeof(GenerationStageCache).Assembly.Location);
            Type protocol = assembly.GetType("BGCS.Infrastructure.Workers.WorkerProtocol", throwOnError: true)!;
            JsonSerializerOptions options = Assert.IsType<JsonSerializerOptions>(protocol.GetProperty("jsonOptions")!.GetValue(null));
            Assert.True(options.IsReadOnly);
            Assert.Same(options, protocol.GetProperty("jsonOptions")!.GetValue(null));
            Assert.Throws<InvalidOperationException>(() => options.PropertyNamingPolicy = null);
            Assert.Throws<InvalidOperationException>(() => options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip);
            Assert.Throws<InvalidOperationException>(() => options.Converters.Clear());
            Assert.Equal(JsonUnmappedMemberHandling.Disallow, options.UnmappedMemberHandling);
        }
        finally { isolated.Unload(); }
    }

    [Fact]
    public async Task RejectedOptionMutationCannotRelaxTheActualFramedUnknownMemberCheck()
    {
        Assert.Throws<InvalidOperationException>(() => WorkerProtocol.jsonOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip);
        using MemoryStream frame = new();
        await WorkerProtocol.WriteAsync(frame, new { value = 42, unexpected = "must reject" }, CancellationToken.None);
        frame.Position = 0;
        await Assert.ThrowsAsync<JsonException>(async () => { _ = await WorkerProtocol.ReadAsync<Probe>(frame, CancellationToken.None); });
    }

    private sealed record Probe(int value);
}
