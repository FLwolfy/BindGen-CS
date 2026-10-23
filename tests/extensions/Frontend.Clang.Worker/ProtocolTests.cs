using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Frontend.Clang.Parsing.Arguments;
using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class ProtocolTests : WorkerTestHost
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(268435457)]
    public async Task InvalidFrameLength_IsRejectedBeforePayloadAllocation(int length)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using MemoryStream stream = new(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await WorkerProtocol.ReadAsync<WorkerHandshake>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedPayload_ReportsClosedTransport()
    {
        byte[] bytes = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 10);
        using MemoryStream stream = new(bytes);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await WorkerProtocol.ReadAsync<WorkerHandshake>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ImmutableNativeGraph_RoundTripsAfterTransport()
    {
        StableId id = new("type:int32");
        NativeType type = new(id, NativeTypeCategory.Primitive, "int", 4, 4);
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8,
            ImmutableArray.Create(type), [], []);
        using MemoryStream stream = new();
        await WorkerProtocol.WriteAsync(stream, new WorkerResponse("request", new FrontendResult(module, new SourceSnapshot([], [])), null), CancellationToken.None);
        stream.Position = 0;
        WorkerResponse response = await WorkerProtocol.ReadAsync<WorkerResponse>(stream, CancellationToken.None);
        Assert.Equal("request", response.requestId);
        Assert.Equal(type, response.result!.module.GetType(id));
        Assert.Equal(id.value, response.result.module.types[0].id.value);
    }

    [Fact]
    public void UserAssertion_DoesNotBecomeVerifiedEvidence()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "configuration");
        string json = JsonSerializer.Serialize(evidence, WorkerProtocol.jsonOptions);
        Assert.Equal(EvidenceLevel.UserAsserted,
            JsonSerializer.Deserialize<Evidence>(json, WorkerProtocol.jsonOptions)!.level);
    }
}
