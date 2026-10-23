using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using BGCS.Analysis.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralValueProtocolTests
{
    private static readonly Evidence s_declared = new(EvidenceLevel.Declared, "typed-fixture");

    [Theory]
    [InlineData("valid")]
    [InlineData("destination")]
    [InlineData("capacity")]
    [InlineData("result")]
    [InlineData("missing-copy")]
    [InlineData("asserted-release")]
    [InlineData("unknown-scope")]
    [InlineData("default-scope")]
    public void CompleteCodeUnitCopyAndCreatingCallReleaseAreIndependentlyChecked(string mutation)
    {
        InteropModule module=Module();
        NativeValueProtocol original=module.GetType(new("holder")).valueProtocol!;
        InteropFunction getter=module.functions.Single(function=>function.id.value=="get");
        StableId output=new("copy:result"),error=new("copy:error");
        OwnershipContract errorOwnership=getter.error!.errorOwnership! with {lifetime=getter.error.errorOwnership!.lifetime with {owner=error}};
        InteropFunction copy=getter with
        {
            id=new("copy"),exportName="copy",
            parameters=[getter.parameters[0] with {id=new("copy:self")},
                new(new("copy:destination"),"destination",new("int-out")),new(new("copy:capacity"),"capacity",new("count")),
                getter.parameters.Single(parameter=>parameter.id==getter.error.valueParameter) with {id=output,type=new("count-out")},
                getter.parameters[^1] with {id=error,ownership=errorOwnership}],
            error=getter.error with {valueParameter=output,errorParameter=error,errorOwnership=errorOwnership}
        };
        NativeValueExport operation=new(NativeValueOperationKind.CopyCodeUnits,copy.id,new("copy:self"),[new("copy:destination"),new("copy:capacity")]);
        NativeValueProtocol protocol=original with
        {
            family=NativeValueFamily.String,
            text=new(4,true,module.byteOrder,s_declared),
            operations=original.operations.Add(operation),
            scopedRelease=new(NativeValueReleaseScope.CreatingCall,s_declared)
        };
        if(mutation=="missing-copy")protocol=protocol with {operations=protocol.operations.Remove(operation)};
        if(mutation=="asserted-release")protocol=protocol with {scopedRelease=new(NativeValueReleaseScope.CreatingCall,new(EvidenceLevel.UserAsserted,"guessed-standard"))};
        if(mutation=="unknown-scope")protocol=protocol with {scopedRelease=new((NativeValueReleaseScope)99,s_declared)};
        if(mutation=="default-scope")protocol=protocol with {scopedRelease=new(default,s_declared)};
        if(mutation is "destination" or "capacity" or "result")
        {
            int index=mutation=="destination"?1:mutation=="capacity"?2:3;
            copy=copy with {parameters=copy.parameters.SetItem(index,copy.parameters[index] with {type=new(mutation=="result"?"int-out":"int")})};
        }
        InteropModule candidate=new(module.libraryId,module.targetId,module.pointerSize,
            module.types.Select(type=>type.id.value=="holder"?type with {valueProtocol=protocol}:type).ToImmutableArray(),module.functions.Add(copy),[]){byteOrder=module.byteOrder};
        if(mutation=="valid")
        {
            new NativeValueProtocolVerifier().Verify(candidate);
            new NativeValueProtocolVerifier().Verify(JsonSerializer.Deserialize<InteropModule>(JsonSerializer.Serialize(candidate))!);
        }
        else Assert.Throws<InvalidOperationException>(()=>new NativeValueProtocolVerifier().Verify(candidate));
    }

    [Fact]
    public void CompleteOpaqueSequence_PreservesUnknownOwnerAndCleanupAffinityAcrossSerialization()
    {
        InteropModule module = Module();
        new NativeValueProtocolVerifier().Verify(module);
        InteropModule restored = JsonSerializer.Deserialize<InteropModule>(JsonSerializer.Serialize(module))!;
        new NativeValueProtocolVerifier().Verify(restored);
        NativeValueProtocol protocol = restored.GetType(new("holder")).valueProtocol!;
        Assert.Equal(NativeLifetimeKind.Unknown, protocol.sequence!.readLifetime.kind);
        Assert.Null(restored.functions.Single(function => function.id == new StableId("construct")).parameters.Single(parameter => parameter.id == new StableId("construct:result")).ownership!.releaseThreadDomain);
        Assert.Equal(NativeValueFamily.Vector, protocol.family);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing-export")]
    [InlineData("missing-accessor")]
    [InlineData("input-coverage")]
    [InlineData("receiver")]
    [InlineData("family")]
    [InlineData("element")]
    [InlineData("packed")]
    [InlineData("boolean")]
    [InlineData("release")]
    [InlineData("period")]
    public void ContradictoryValueContract_IsRejectedBeforeAnyProjection(string mutation)
    {
        InteropModule module = Module();
        NativeValueProtocol protocol = module.GetType(new("holder")).valueProtocol!;
        NativeValueExport getter = protocol.operations.Single(operation => operation.kind == NativeValueOperationKind.GetAt);
        protocol = mutation switch
        {
            "duplicate" => protocol with { operations = protocol.operations.Add(getter) },
            "missing-export" => protocol with { operations = protocol.operations.Replace(getter, getter with { function = new("absent") }) },
            "missing-accessor" => protocol with { operations = protocol.operations.Remove(getter) },
            "input-coverage" => protocol with { operations = protocol.operations.Replace(getter, getter with { arguments = [] }) },
            "receiver" => protocol with { operations = protocol.operations.Replace(getter, getter with { receiverParameter = new("get:index") }) },
            "family" => protocol with { family = NativeValueFamily.Optional, sequence = null },
            "element" => protocol with { elements = [protocol.elements[0] with { carrierType = new("count") }] },
            "packed" => protocol with { sequence = protocol.sequence! with { isPackedBoolean = true } },
            "boolean" => protocol with { elements = [protocol.elements[0] with { isBoolean = true }], sequence = protocol.sequence! with { isPackedBoolean = true } },
            "release" => protocol with { releaseExport = new("error-release") },
            "period" => protocol with { period = new(1, 1000, s_declared) },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        InteropModule invalid = new(module.libraryId, module.targetId, module.pointerSize,
            module.types.Select(type => type.id == new StableId("holder") ? type with { valueProtocol = protocol } : type).ToImmutableArray(), module.functions, [])
        { byteOrder = module.byteOrder };
        Assert.Throws<InvalidOperationException>(() => new NativeValueProtocolVerifier().Verify(invalid));
    }

    [Fact]
    public void ConstMutationOrChangedIndexWidth_IsRejectedAgainstTheActualCFunction()
    {
        foreach (string mutation in new[] { "const", "index" })
        {
            InteropModule module = Module();
            InteropFunction setter = module.functions.Single(function => function.id == new StableId("set"));
            InteropFunction changed = setter with { parameters = setter.parameters.Select(parameter => parameter.id.value == (mutation == "const" ? "set:self" : "set:index")
                ? parameter with { type = new(mutation == "const" ? "const-holder-pointer" : "int") } : parameter).ToImmutableArray() };
            InteropModule invalid = new(module.libraryId, module.targetId, module.pointerSize, module.types,
                module.functions.Select(function => function.id == setter.id ? changed : function).ToImmutableArray(), []) { byteOrder = module.byteOrder };
            Assert.Throws<InvalidOperationException>(() => new NativeValueProtocolVerifier().Verify(invalid));
        }
    }

    [Fact]
    public void BorrowedInputOnlySpan_DoesNotInventOrRequireAHolderDestructionExport()
    {
        InteropModule original = Module();
        NativeValueProtocol protocol = original.GetType(new("holder")).valueProtocol!;
        protocol = protocol with
        {
            family = NativeValueFamily.Span, releaseExport = null,
            sequence = protocol.sequence! with { isBorrowedView = true },
            operations = protocol.operations.Where(operation => operation.kind is NativeValueOperationKind.Count or NativeValueOperationKind.GetAt or NativeValueOperationKind.SetAt).ToImmutableArray()
        };
        InteropModule input = new(original.libraryId, original.targetId, original.pointerSize,
            original.types.Select(type => type.valueProtocol is null ? type : type with { valueProtocol = protocol }).ToImmutableArray(),
            original.functions.Where(function => function.id.value is not ("construct" or "destroy" or "append")).ToImmutableArray(), []) { byteOrder = original.byteOrder };
        new NativeValueProtocolVerifier().Verify(input);
        Assert.Equal(NativeLifetimeKind.Unknown, protocol.sequence!.readLifetime.kind);
    }

    [Fact]
    public void FreshOwnedHolder_CannotOmitItsMatchingDestructionExport()
    {
        InteropModule original = Module();
        InteropModule invalid = new(original.libraryId, original.targetId, original.pointerSize,
            original.types.Select(type => type.valueProtocol is null ? type : type with { valueProtocol = type.valueProtocol with { releaseExport = null } }).ToImmutableArray(),
            original.functions, []) { byteOrder = original.byteOrder };
        Assert.Throws<InvalidOperationException>(() => new NativeValueProtocolVerifier().Verify(invalid));
    }

    private static InteropModule Module()
    {
        List<InteropType> types = [];
        InteropType Scalar(string id, int width, bool signed, string primitive) => new(new(id), id, NativeTypeCategory.Primitive, width, width) { isSigned = signed, primitiveKind = primitive };
        InteropType integer = Scalar("int", 4, true, "Int"), count = Scalar("count", 8, false, "UnsignedLongLong");
        InteropType empty = new(new("void"), "void", NativeTypeCategory.Primitive, 0, null) { primitiveKind = "Void" };
        InteropType holder = new(new("holder"), "holder", NativeTypeCategory.Record, null, null) { sourceNativeType = new("source-vector") };
        InteropType constant = new(new("const-holder"), "const-holder", NativeTypeCategory.Qualified, null, null) { elementType = holder.id, qualifier = "Const" };
        InteropType error = new(new("error"), "error", NativeTypeCategory.Record, null, null);
        types.AddRange([integer, count, empty, holder, constant, error]);
        InteropType Pointer(string id, InteropType element)
        {
            InteropType type = new(new(id), id, NativeTypeCategory.Pointer, 8, 8) { elementType = element.id };
            types.Add(type);
            return type;
        }
        InteropType holderPointer = Pointer("holder-pointer", holder), constHolderPointer = Pointer("const-holder-pointer", constant);
        InteropType holderOut = Pointer("holder-out", holderPointer), intOut = Pointer("int-out", integer), countOut = Pointer("count-out", count);
        InteropType errorPointer = Pointer("error-pointer", error), errorOut = Pointer("error-out", errorPointer);
        OwnershipContract Borrow() => new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, s_declared), s_declared);
        InteropFunction Function(string id, bool? receiver, (string Name, StableId Type)[] arguments, InteropType? output)
        {
            List<InteropParameter> parameters = [];
            if (receiver is bool mutable) parameters.Add(new(new(id + ":self"), "self", mutable ? holderPointer.id : constHolderPointer.id) { ownership = Borrow() });
            parameters.AddRange(arguments.Select(argument => new InteropParameter(new(id + ":" + argument.Name), argument.Name, argument.Type)));
            StableId? result = output is null ? null : new(id + ":result");
            if (result is StableId value) parameters.Add(new(value, "result", output!.id) { ownership = id == "construct"
                ? new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, value, s_declared), s_declared) { releaseFunction = new("destroy"), allocator = new("holder-allocator") } : null });
            StableId errorId = new(id + ":error");
            OwnershipContract ownedError = new(NativeOwnershipKind.Owned, new(NativeLifetimeKind.Owner, errorId, s_declared), s_declared)
            { releaseFunction = new("error-release"), allocator = new("error-allocator"), releaseThreadDomain = "any" };
            parameters.Add(new(errorId, "error", errorOut.id) { ownership = ownedError });
            return new(new(id), id, integer.id, parameters.ToImmutableArray(), "C")
            { error = new("0", s_declared) { valueParameter = result, errorParameter = errorId, errorOwnership = ownedError } };
        }
        InteropFunction construct = Function("construct", null, [], holderOut), destroy = Function("destroy", true, [], null);
        InteropFunction length = Function("count-function", false, [], countOut), get = Function("get", false, [("index", count.id)], intOut);
        InteropFunction set = Function("set", true, [("index", count.id), ("value", integer.id)], null), append = Function("append", true, [("value", integer.id)], null);
        InteropFunction errorRelease = new(new("error-release"), "error_release", empty.id, [new(new("error-free:input"), "error", errorPointer.id)], "C");
        NativeValueExport Access(InteropFunction function, NativeValueOperationKind kind) => new(kind, function.id,
            kind == NativeValueOperationKind.Construct ? null : function.parameters[0].id,
            function.parameters.Where(parameter => parameter.id != function.error!.errorParameter && parameter.id != function.error.valueParameter
                && parameter.name != "self").Select(parameter => parameter.id).ToImmutableArray());
        NativeValueProtocol protocol = new(new("vector-protocol"), NativeValueFamily.Vector, holder.id, holder.sourceNativeType!.Value,
            [Access(construct, NativeValueOperationKind.Construct), Access(length, NativeValueOperationKind.Count), Access(get, NativeValueOperationKind.GetAt),
                Access(set, NativeValueOperationKind.SetAt), Access(append, NativeValueOperationKind.Append)], [new(new("source-int"), integer.id, false)], s_declared)
        { releaseExport = destroy.id, sequence = new(s_declared) };
        return new("fixture", "windows-x64-msvc", 8, types.Select(type => type.id == holder.id ? type with { valueProtocol = protocol } : type).ToImmutableArray(),
            [construct, destroy, length, get, set, append, errorRelease], []) { byteOrder = NativeByteOrder.LittleEndian };
    }
}
