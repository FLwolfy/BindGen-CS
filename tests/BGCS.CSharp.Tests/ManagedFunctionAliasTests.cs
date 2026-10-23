using System;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedFunctionAliasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingOrDanglingAliasesFailBeforeAnyArtifactWrite(bool dangling)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropFunction function = new(new("read"), "read", integer.id, [new(new("input"), "input", integer.id)], "C");
        ManagedApiModel model = new DefaultManagedProjection().Project(new("fixture", "windows-x64-msvc", 8, [integer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian },
            new("Generated.Aliases", "Api"), CancellationToken.None);
        model = model with { aliases = dangling ? [new(new("missing"), "Other")] : [new(function.id, "Read")] };
        NoWrites sink = new();
        if (dangling)
            Assert.Contains("absent native operation", Assert.Throws<ArgumentException>(() => new CSharpTestPlanner().Plan(model, new("unused", sink))).Message, StringComparison.Ordinal);
        else
            Assert.Contains("Managed overload 'RawApi.Read", Assert.Throws<InvalidOperationException>(() => new CSharpTestPlanner().Plan(model, new("unused", sink))).Message, StringComparison.Ordinal);
        Assert.Equal(0, sink.writes);
    }

    private sealed class NoWrites : IArtifactSink
    {
        internal int writes { get; private set; }
        public string WriteText(string relativePath, string content) { writes++; return relativePath; }
    }
}
