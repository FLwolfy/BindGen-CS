using System;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class TextOwnershipProjectionTests
{
    [Theory]
    [InlineData("parameter-owned")]
    [InlineData("text-owned")]
    [InlineData("owner-lifetime")]
    [InlineData("inferred-lifetime")]
    [InlineData("borrowed-call")]
    public async Task TemporaryText_RespectsBothPointerAndStringOwnership(string kind)
    {
        Evidence explicitEvidence = new(EvidenceLevel.UserAsserted, "fixture:exact-native-ownership");
        NativeOwnershipKind ownershipKind = kind.EndsWith("owned", StringComparison.Ordinal) ? NativeOwnershipKind.Owned : NativeOwnershipKind.Borrowed;
        Evidence lifetimeEvidence = kind == "inferred-lifetime" ? new(EvidenceLevel.Inferred, "fixture:unproven-lifetime") : explicitEvidence;
        OwnershipContract ownership = new(ownershipKind, new(kind == "owner-lifetime" ? NativeLifetimeKind.Owner : NativeLifetimeKind.Call, null, lifetimeEvidence), explicitEvidence);
        InteropType octet = new(new("byte"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType pointer = new(new("text-pointer"), "unsigned char*", NativeTypeCategory.Pointer, 8, 8) { elementType = octet.id };
        StringContract text = new(NativeTextEncoding.Utf8, 1, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input,
            new(NativeLifetimeKind.Call, null, explicitEvidence), explicitEvidence)
        { zeroTerminated = true, allowsNull = false, retainsPointer = false, ownership = kind == "text-owned" ? ownership : null };
        InteropParameter parameter = new(new("text"), "text", pointer.id) { text = text, ownership = kind == "text-owned" ? null : ownership };
        InteropFunction function = new(new("consume"), "consume", integer.id, [parameter], "C");
        InteropModule module = new("ownership", "windows-x64-msvc", 8, [integer, octet, pointer], [function], []);
        var model = new DefaultManagedProjection().Project(module, new("Generated.Ownership", "TextApi"), CancellationToken.None);
        Assert.Equal(kind == "borrowed-call" ? 1 : 0, Assert.Single(model.methods).stringInputs.Length);
        NeutralBackendTests.MemorySink sink = new();
        await new CSharpBackend().EmitAsync(model, new("unused", sink), CancellationToken.None);
        Assert.Contains("public int Consume(byte* text)", sink.sources["Api/TextApi.Raw.g.cs"]);
        Assert.Equal(kind == "borrowed-call", sink.sources["Api/TextApi.Methods.g.cs"].Contains("public int Consume(string text)", StringComparison.Ordinal));
    }
}
