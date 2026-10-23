using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ArtifactSinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backend_UsesCallerArtifactStoreAndReturnsEveryLogicalArtifactAndDigest(bool singleFile)
    {
        MemorySink sink = new();
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4)
        { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("value"), "Value", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Value", fields = [new(new("field"), "number", integer.id, 0, 0, null)] };
        InteropModule module = new("example", "windows-x64-msvc", 8, [integer, record], [], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Example", "Api"), CancellationToken.None);
        var outputs = await new CSharpTestBackend().EmitAsync(model, new("native", sink) { singleFile = singleFile }, CancellationToken.None);
        Assert.Equal(sink.sources.Count, outputs.Length);
        Assert.Equal(sink.sources.Keys.Order(StringComparer.Ordinal), outputs.Select(output => output.relativePath).Order(StringComparer.Ordinal));
        foreach (ArtifactDescriptor output in outputs)
        {
            Assert.Equal("text/x-csharp", output.mediaType);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sink.sources[output.relativePath]))), output.digest.value);
            Assert.DoesNotContain("memory:", output.relativePath, StringComparison.Ordinal);
        }
        Assert.Contains("struct Value", string.Join("\n", sink.sources.Values), StringComparison.Ordinal);
        if (singleFile) Assert.Equal("Bindings.cs", Assert.Single(outputs).relativePath);
        else Assert.Contains(outputs, output => output.relativePath == "Types/Value.g.cs");
    }

    private sealed class MemorySink : IArtifactSink
    {
        internal Dictionary<string, string> sources { get; } = new(StringComparer.Ordinal);

        public string WriteText(string relativePath, string content)
        {
            sources.Add(relativePath, content);
            return "memory:" + relativePath;
        }
    }
}
