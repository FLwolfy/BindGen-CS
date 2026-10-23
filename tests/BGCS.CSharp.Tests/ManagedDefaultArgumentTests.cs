using System;
using System.Collections.Immutable;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ManagedDefaultArgumentTests
{
    [Theory]
    [InlineData("overflow", "outside its exact friendly type range")]
    [InlineData("negative-unsigned", "outside its exact friendly type range")]
    [InlineData("null-integer", "nullable friendly string or raw pointer")]
    [InlineData("float-integer", "finite value within its exact friendly type")]
    [InlineData("injected-reference", "qualified identifier")]
    [InlineData("unknown-parameter", "identify exactly one native parameter")]
    [InlineData("unbounded-expansion", "exceeds its explicit limit")]
    [InlineData("ambiguous-omission", "Managed overload")]
    public void InvalidOrAmbiguousConveniencesFailBeforeAnyArtifactWrite(
        string mutation,
        string reason
    ) {
        bool unsigned = mutation is "overflow" or "negative-unsigned";
        InteropType integer = new(new("integer"), unsigned ? "unsigned char" : "int", NativeTypeCategory.Primitive,
            unsigned ? 1 : 4, unsigned ? 1 : 4)
        { primitiveKind = unsigned ? "UnsignedChar" : "Int", isSigned = !unsigned };
        InteropFunction function = new(new("read"), "read", integer.id,
            [new(new("a"), "a", integer.id), new(new("b"), "b", integer.id)], "C");
        ManagedDefaultValue value = mutation switch
        {
            "overflow" => new(ManagedDefaultValueKind.Integer, "256"),
            "negative-unsigned" => new(ManagedDefaultValueKind.Integer, "-1"),
            "null-integer" => new(ManagedDefaultValueKind.Null),
            "float-integer" => new(ManagedDefaultValueKind.FloatingPoint, "1.5"),
            "injected-reference" => new(ManagedDefaultValueKind.ConstantReference, "System.Environment.Exit(1)"),
            _ => new(ManagedDefaultValueKind.Integer, "1")
        };
        ImmutableArray<ManagedDefaultArgumentRequest> defaults = mutation is "unbounded-expansion" or "ambiguous-omission"
            ? [new("a", value), new("b", value)] : [new(mutation == "unknown-parameter" ? "absent" : "b", value)];
        NoWrites sink = new();
        Action operation = () =>
        {
            ManagedApiModel model = new DefaultManagedProjection().Project(new("fixture", "windows-x64-msvc", 8, [integer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian },
                new("Generated.Defaults", "Api")
                { functions = [new("read") { defaults = defaults }], defaultOverloadLimit = mutation == "unbounded-expansion" ? 1 : 128 }, CancellationToken.None);
            _ = new CSharpTestPlanner().Plan(model, new("unused", sink));
        };
        Exception error = mutation == "ambiguous-omission" ? Assert.Throws<InvalidOperationException>(operation) : Assert.Throws<ArgumentException>(operation);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, sink.writes);
    }

    private sealed class NoWrites : IArtifactSink
    {
        internal int writes { get; private set; }
        public string WriteText(
            string relativePath,
            string content
        ) { writes++; return relativePath; }
    }
}
