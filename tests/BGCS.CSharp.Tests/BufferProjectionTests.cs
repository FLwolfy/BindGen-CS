using System;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class BufferProjectionTests
{
    [Theory]
    [InlineData("retained")]
    [InlineData("unknown-null")]
    [InlineData("inferred")]
    [InlineData("unbounded")]
    [InlineData("unknown-direction")]
    [InlineData("different-element")]
    [InlineData("separate-capacity")]
    [InlineData("unknown-length-unit")]
    [InlineData("const-output")]
    public async Task UnsupportedBufferContract_KeepsOnlyItsRawNativeEntry(string fault)
    {
        Evidence declared = new(EvidenceLevel.UserAsserted, "test:bounded-native-buffer");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType octet = new(new("byte"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType constant = new(new("const-int"), "const int", NativeTypeCategory.Qualified, 4, 4) { elementType = integer.id, qualifier = "Const" };
        InteropType pointer = new(new("pointer"), "const int*", NativeTypeCategory.Pointer, 8, 8) { elementType = constant.id };
        InteropParameter length = new(new("length"), "length", octet.id);
        BufferContract valid = new(integer.id, NativeLengthUnit.Elements, NativeParameterDirection.Input, new(NativeLifetimeKind.Call, null, declared), declared)
        { lengthParameter = length.id, retainsPointer = false, allowsNull = true };
        BufferContract invalid = fault switch
        {
            "retained" => valid with { retainsPointer = true },
            "unknown-null" => valid with { allowsNull = null },
            "inferred" => valid with { evidence = new(EvidenceLevel.Inferred, "argument-name") },
            "unbounded" => valid with { lengthParameter = null },
            "unknown-direction" => valid with { direction = NativeParameterDirection.Unknown },
            "different-element" => valid with { elementType = octet.id },
            "separate-capacity" => valid with { capacityParameter = new("capacity") },
            "unknown-length-unit" => valid with { lengthUnit = NativeLengthUnit.Unknown },
            "const-output" => valid with { direction = NativeParameterDirection.Output },
            _ => throw new ArgumentException("Unknown fixture.")
        };
        InteropParameter data = new(new("data"), "data", pointer.id) { buffer = invalid };
        InteropFunction function = new(new("sum"), "sum", integer.id, [data, length], "C");
        InteropModule module = new("buffers", "windows-x64-msvc", 8, [integer, octet, constant, pointer], [function], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Buffers", "BufferApi"), CancellationToken.None);
        Assert.Empty(Assert.Single(model.methods).bufferInputs);
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpTestBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        Assert.Contains("public int Sum(int* data, byte length)", sink.sources["Api/BufferApi.Raw.g.cs"]);
        Assert.DoesNotContain("public int Sum", sink.sources["Api/BufferApi.Methods.g.cs"]);
    }
}
