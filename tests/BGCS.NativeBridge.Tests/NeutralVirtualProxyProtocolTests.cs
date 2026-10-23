using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using BGCS.Analysis.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralVirtualProxyProtocolTests
{
    private static readonly Evidence s_declared = new(EvidenceLevel.Declared, "native-proxy-fixture");

    [Fact]
    public void TypedConstructionCastAndCallbacks_RoundTripWithoutInventingRetentionOrNativeQuiescence()
    {
        InteropModule original = Module();
        new VirtualProxyProtocolVerifier().Verify(original);
        InteropModule restored = JsonSerializer.Deserialize<InteropModule>(JsonSerializer.Serialize(original))!;
        new VirtualProxyProtocolVerifier().Verify(restored);

        VirtualProxyContract protocol = Assert.Single(restored.virtualProxies);
        Assert.Equal(NativeLifetimeKind.Unknown, protocol.lifetime.kind);
        Assert.Equal(EvidenceLevel.Unknown, protocol.lifetime.evidence.level);
        Assert.Equal(NativeQuiescenceKind.Unknown, protocol.quiescence);
        Assert.Equal(new StableId("destroy"), protocol.ownership.releaseFunction);
        Assert.Equal(new StableId("allocator"), protocol.ownership.allocator);
        VirtualProxySlotContract slot = Assert.Single(protocol.slots);
        Assert.Equal("Compute", slot.sourceName);
        Assert.Equal(new StableId("source-value"), Assert.Single(slot.parameters).sourceDeclaration);
        Assert.Equal(2, slot.result!.index);
        Assert.Equal(new StableId("integer"), slot.result.valueType);
    }

    [Theory]
    [InlineData("missing-export")]
    [InlineData("cleanup-domain")]
    [InlineData("cleanup-lifetime")]
    [InlineData("missing-slot")]
    [InlineData("factory-position")]
    [InlineData("missing-source-name")]
    [InlineData("callback-type")]
    [InlineData("variadic-callback")]
    [InlineData("argument-index")]
    [InlineData("argument-source")]
    [InlineData("result-index")]
    [InlineData("result-source")]
    [InlineData("interface-origin")]
    [InlineData("cast-role")]
    [InlineData("cleanup-owner")]
    [InlineData("invented-quiescence")]
    public void InconsistentNativeProtocol_IsRejectedBeforeProjection(string mutation)
    {
        InteropModule valid = Module();
        VirtualProxyContract proxy = valid.virtualProxies[0];
        VirtualProxySlotContract slot = proxy.slots[0];
        ImmutableArray<InteropFunction> functions = valid.functions;
        ImmutableArray<InteropType> types = valid.types;
        switch (mutation)
        {
            case "missing-export": proxy = proxy with { destroyFunction = new("absent") }; break;
            case "cleanup-domain": proxy = proxy with { ownership = proxy.ownership with { releaseFunction = proxy.createFunction } }; break;
            case "cleanup-lifetime": proxy = proxy with { ownership = proxy.ownership with { lifetime = new(NativeLifetimeKind.Call, null, s_declared) } }; break;
            case "missing-slot": proxy = proxy with { slots = [] }; break;
            case "factory-position": proxy = proxy with { slots = [slot with { factoryParameter = proxy.userDataParameter }] }; break;
            case "missing-source-name": proxy = proxy with { slots = [slot with { sourceName = "" }] }; break;
            case "callback-type": proxy = proxy with { slots = [slot with { callableType = new("address") }] }; break;
            case "variadic-callback": types = types.Select(type => type.id == slot.callableType ? type with { isVariadic = true } : type).ToImmutableArray(); break;
            case "argument-index": proxy = proxy with { slots = [slot with { parameters = [slot.parameters[0] with { index = 0 }] }] }; break;
            case "argument-source": proxy = proxy with { slots = [slot with { parameters = [slot.parameters[0] with { sourceDeclaration = null }] }] }; break;
            case "result-index": proxy = proxy with { slots = [slot with { result = slot.result! with { index = 1 } }] }; break;
            case "result-source": proxy = proxy with { slots = [slot with { result = slot.result! with { sourceDeclaration = new("invented") } }] }; break;
            case "interface-origin": proxy = proxy with { sourceInterface = new("unrelated-interface") }; break;
            case "cast-role": functions = functions.Select(function => function.id == proxy.interfaceCastFunction ? function with { sourceRole = "Reinterpret" } : function).ToImmutableArray(); break;
            case "cleanup-owner": functions = functions.Select(function => function.id == proxy.destroyFunction ? function with { sourceOwnerType = proxy.sourceInterface } : function).ToImmutableArray(); break;
            case "invented-quiescence": proxy = proxy with { quiescence = NativeQuiescenceKind.NoActiveNativeInvocations }; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        InteropModule invalid = new(valid.libraryId, valid.targetId, valid.pointerSize, types, functions, [])
        { byteOrder = valid.byteOrder, virtualProxies = [proxy] };
        Assert.Throws<InvalidOperationException>(() => new VirtualProxyProtocolVerifier().Verify(invalid));
    }

    private static InteropModule Module()
    {
        StableId integer = new("integer"), nothing = new("void"), address = new("address"), pointer = new("integer-pointer"), callback = new("callback");
        StableId concrete = new("proxy"), nativeInterface = new("interface"), concreteAddress = new("proxy-pointer"), concreteOutput = new("proxy-output");
        StableId interfaceAddress = new("interface-pointer"), interfaceOutput = new("interface-output"), sourceProxy = new("source-proxy"), sourceInterface = new("source-interface");
        InteropType[] types =
        [
            new(integer, "int32_t", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true },
            new(nothing, "void", NativeTypeCategory.Primitive, null, null) { primitiveKind = "Void" },
            new(address, "void*", NativeTypeCategory.Pointer, 8, 8) { elementType = nothing },
            new(pointer, "int32_t*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer },
            new(concrete, "proxy", NativeTypeCategory.Record, null, null) { sourceGeneratedType = sourceProxy },
            new(nativeInterface, "interface", NativeTypeCategory.Record, null, null) { sourceNativeType = sourceInterface },
            new(concreteAddress, "proxy*", NativeTypeCategory.Pointer, 8, 8) { elementType = concrete },
            new(concreteOutput, "proxy**", NativeTypeCategory.Pointer, 8, 8) { elementType = concreteAddress },
            new(interfaceAddress, "interface*", NativeTypeCategory.Pointer, 8, 8) { elementType = nativeInterface },
            new(interfaceOutput, "interface**", NativeTypeCategory.Pointer, 8, 8) { elementType = interfaceAddress },
            new(callback, "callback", NativeTypeCategory.Function, null, null)
            { returnType = integer, parameterTypes = [address, integer, pointer], callingConvention = "C" }
        ];
        OwnershipContract owned = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, new("create:result"), s_declared), s_declared)
        { releaseFunction = new("destroy"), allocator = new("allocator") };
        InteropFunction create = new(new("create"), "create", integer,
            [new(new("create:user-data"), "userData", address), new(new("create:slot"), "slot0", address),
                new(new("create:result"), "result", concreteOutput) { ownership = owned }, new(new("create:error"), "error", address)], "C")
        { error = new("0", s_declared) { valueParameter = new("create:result"), errorParameter = new("create:error") } };
        InteropFunction destroy = new(new("destroy"), "destroy", integer,
            [new(new("destroy:self"), "self", concreteAddress), new(new("destroy:error"), "error", address)], "C")
        { sourceRole = "Destroy", sourceGeneratedOwnerType = sourceProxy, error = new("0", s_declared) { errorParameter = new("destroy:error") } };
        InteropFunction cast = new(new("cast"), "cast", integer,
            [new(new("cast:self"), "self", concreteAddress), new(new("cast:result"), "result", interfaceOutput)
                { ownership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Owner, new("cast:self"), s_declared), s_declared) },
                new(new("cast:error"), "error", address)], "C")
        { sourceRole = "BaseCast", sourceGeneratedOwnerType = sourceProxy, error = new("0", s_declared) { valueParameter = new("cast:result"), errorParameter = new("cast:error") } };
        VirtualProxyValueContract input = new(new("slot:value"), 1, integer, integer, new("source-integer"), "value")
        { sourceDeclaration = new("source-value"), sourceCategory = NativeTypeCategory.Primitive, sourcePrimitiveKind = "Int" };
        VirtualProxyValueContract output = new(new("slot:result"), 2, pointer, integer, new("source-integer"), "result")
        { sourceCategory = NativeTypeCategory.Primitive, sourcePrimitiveKind = "Int" };
        VirtualProxySlotContract slot = new(new("slot"), new("source-method"), "Compute", callback, new("create:slot"), new("slot:user-data"), [input], output)
        { sourceSignature = "Sample::Calculator::Compute(int) const" };
        VirtualProxyContract protocol = new(new("virtual-proxy"), "calculator", sourceInterface, concrete, nativeInterface, create.id, destroy.id, cast.id,
            new("create:user-data"), new("create:result"), new("destroy:self"), new("cast:self"), new("cast:result"), owned, [slot], s_declared)
        { sourceQualifiedName = "Sample::Calculator" };
        return new("fixture", "windows-x64-msvc", 8, [.. types], [create, destroy, cast], [])
        { byteOrder = NativeByteOrder.LittleEndian, virtualProxies = [protocol] };
    }
}
