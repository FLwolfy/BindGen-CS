using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Infrastructure.IO;
using BGCS.Validation;
using Xunit;

namespace BGCS.Tests;

public sealed class ManagedExternalTypeProbeTests
{
    [Fact]
    public async Task ActualClosedGenericAndNumericsFieldsAreMeasuredOutsideTheHost()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs external generic " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "Consumer.cs");
            await File.WriteAllTextAsync(source, """
                #if !TRACE || !EXTERNAL_PROBE
                #error Explicit managed symbols must append the SDK default TRACE symbol.
                #endif
                using System.Runtime.InteropServices;
                namespace Consumer;
                public struct Pair<T> where T : unmanaged { public T X; public T Y; }
                [StructLayout(LayoutKind.Explicit, Size=16)]
                public struct Reordered { [FieldOffset(8)] public float X; [FieldOffset(0)] public float Y; }
                """);
            ManagedExternalTypeProbeResult result = await new ManagedExternalTypeProbe().ProbeAsync(
                [Projection("Consumer.Pair<float>"), Projection("System.Numerics.Vector2"), Projection("Consumer.Reordered")],
                new() { sources = [source], defines = ["EXTERNAL_PROBE"] }, root, TimeSpan.FromMinutes(3));
            Assert.Equal(3, result.facts.Length);
            foreach (ManagedTypeStorageFact fact in result.facts)
            {
                Assert.True(fact.isReferenceFree);
                Assert.Equal(EvidenceLevel.Verified, fact.evidence.level);
                Assert.All(fact.fields, field => { Assert.Equal(ManagedScalarKind.BinaryFloat, field.scalarKind); Assert.Equal(4, field.size); Assert.True(field.isWritable); });
                Assert.Equal(IntPtr.Size, fact.probe.pointerSize);
            }
            Assert.Equal(8, result.facts[0].size);
            Assert.Equal(4, result.facts[0].alignment);
            Assert.Equal(8, result.facts[1].size);
            Assert.Equal(16, result.facts[2].size);
            Assert.Equal(8, result.facts[2].fields.Single(field => field.name == "X").offset);
            Assert.Contains(result.inputs.files, input => input.path == source);
            Assert.True(SourceSnapshotVerifier.Matches(result.inputs));
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "Probe");
            await File.AppendAllTextAsync(source, "\n// changed after measurement\n");
            Assert.False(SourceSnapshotVerifier.Matches(result.inputs));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("public struct Rejected { public string X; public int Y; }")]
    [InlineData("public struct Rejected { public readonly int X; public int Y; }")]
    [InlineData("public struct Rejected { public int X; private int Y; }")]
    [InlineData("public struct Rejected { public int X; public int Y; public int Unmapped; }")]
    public async Task ReferenceReadonlyPrivateAndUnmappedFieldsAreRejected(string declaration)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs external refused " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "Consumer.cs");
            await File.WriteAllTextAsync(source, "namespace Consumer; " + declaration);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ManagedExternalTypeProbe().ProbeAsync(
                [Projection("Consumer.Rejected")], new() { sources = [source] }, root, TimeSpan.FromMinutes(3)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAStorageReceipt()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ManagedExternalTypeProbe().ProbeAsync(
            [Projection("System.Numerics.Vector2")], new(), Path.GetTempPath(), TimeSpan.FromMinutes(1), canceled.Token));
    }

    private static ExternalTypeProjection Projection(string type) => new(new("point"), type, [new(new("x"), "X"), new(new("y"), "Y")]);
}
