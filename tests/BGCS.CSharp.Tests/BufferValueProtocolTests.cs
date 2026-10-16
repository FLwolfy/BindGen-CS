using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class BufferValueProtocolTests
{
    [Theory]
    [InlineData(NativeLengthUnit.Elements)]
    [InlineData(NativeLengthUnit.Bytes)]
    public void ExplicitCapacityAndOutputExtentPreserveEveryNativeChannel(NativeLengthUnit unit)
    {
        var (module, function) = Module();
        InteropParameter buffer = function.parameters[0] with { buffer = function.parameters[0].buffer! with { lengthUnit = unit } };
        function = function with { parameters = function.parameters.SetItem(0, buffer) };
        CSharpBufferProtocolPlan plan = new BufferProtocolPlanner().Plan(module, function, buffer, Resolve);
        Assert.Equal(0, plan.parameterIndex);
        Assert.Equal(1, plan.boundIndex);
        Assert.True(plan.boundIsCapacity);
        Assert.Equal(2, plan.writtenCountIndex);
        Assert.Equal("int", plan.writtenCountType);
        Assert.True(plan.writtenCountIsSigned);
        Assert.Equal(4, plan.elementSize);
        Assert.Equal(4, plan.writtenCountAlignment);
        Assert.Equal(unit, plan.lengthUnit);
    }

    [Theory]
    [InlineData("missing-capacity")]
    [InlineData("distinct-length")]
    [InlineData("retained")]
    [InlineData("unknown-retention")]
    [InlineData("unknown-null")]
    [InlineData("count-is-bound")]
    [InlineData("input-only")]
    [InlineData("count-is-float")]
    [InlineData("count-is-const")]
    [InlineData("count-overaligned")]
    [InlineData("shared-count")]
    public void IncompleteOrAmbiguousCapacityProtocolsAreRejected(string mutation)
    {
        var (module, function) = Module();
        InteropParameter buffer = function.parameters[0];
        BufferContract contract = buffer.buffer!;
        switch (mutation)
        {
            case "missing-capacity": contract = contract with { capacityParameter = null, lengthParameter = new("bound") }; break;
            case "distinct-length": contract = contract with { lengthParameter = new("count") }; break;
            case "retained": contract = contract with { retainsPointer = true }; break;
            case "unknown-retention": contract = contract with { retainsPointer = null }; break;
            case "unknown-null": contract = contract with { allowsNull = null }; break;
            case "count-is-bound": contract = contract with { writtenCountParameter = new("bound") }; break;
            case "input-only": contract = contract with { direction = NativeParameterDirection.Input }; break;
            case "count-is-float": module = ChangeCount(module, new(new("count-value"), "float", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Float" }); break;
            case "count-is-const": module = ChangeCount(module, module.GetType(new("count-value")) with { qualifier = "Const" }); break;
            case "count-overaligned": module = ChangeCount(module, module.GetType(new("count-value")) with { alignment = 32 }); break;
            case "shared-count": function = function with { parameters = function.parameters.Add(buffer with { id = new("other-buffer") }) }; break;
        }
        buffer = buffer with { buffer = contract };
        function = function with { parameters = function.parameters.SetItem(0, buffer) };
        Assert.Throws<NotSupportedException>(() => new BufferProtocolPlanner().Plan(module, function, buffer, Resolve));
    }

    [Theory]
    [InlineData(NativeParameterDirection.Input)]
    [InlineData(NativeParameterDirection.Output)]
    [InlineData(NativeParameterDirection.InputOutput)]
    public void ExplicitScalarValuePointersCarryCheckedAddressAllocationAndDirection(NativeParameterDirection direction)
    {
        var (module, function) = Module();
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:value");
        InteropParameter parameter = function.parameters[0] with { buffer = null, singleValue = new(direction, new(NativeLifetimeKind.Call, null, evidence), evidence)
            { retainsPointer = false, allowsNull = false } };
        function = function with { parameters = function.parameters.SetItem(0, parameter) };
        CSharpValueParameterPlan plan = new ValueParameterPlanner().Plan(module, function, parameter, Resolve, new Dictionary<StableId, CSharpStoragePlan>());
        Assert.Equal(direction, plan.direction);
        Assert.Equal(CSharpValueAllocationMode.AddressCheckedPinnedBorrow, plan.allocationMode);
        Assert.Equal(4, plan.alignment);
        Assert.Equal("int", plan.elementType);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("nullable")]
    [InlineData("retained")]
    [InlineData("owner-lifetime")]
    [InlineData("const-output")]
    [InlineData("overaligned")]
    [InlineData("boolean32")]
    [InlineData("enum")]
    [InlineData("buffer-conflict")]
    public void ValuePointerConvenienceNeverGuessesMissingLifetimeOrStorageEvidence(string mutation)
    {
        var (module, function) = Module();
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:value");
        ValueParameterContract value = new(NativeParameterDirection.Output, new(NativeLifetimeKind.Call, null, evidence), evidence)
            { retainsPointer = false, allowsNull = false };
        switch (mutation)
        {
            case "unknown": value = value with { retainsPointer = null }; break;
            case "nullable": value = value with { allowsNull = true }; break;
            case "retained": value = value with { retainsPointer = true }; break;
            case "owner-lifetime": value = value with { lifetime = new(NativeLifetimeKind.Owner, null, evidence) }; break;
            case "const-output": module = ChangeElement(module, module.GetType(new("int")) with { qualifier = "Const" }); break;
            case "overaligned": value = value with { requiredAlignment = 32 }; break;
            case "boolean32": module = ChangeElement(module, module.GetType(new("int")) with { primitiveKind = "Bool", size = 4, alignment = 4 }); break;
            case "enum": module = ChangeElement(module, module.GetType(new("int")) with { category = NativeTypeCategory.Enumeration }); break;
        }
        InteropParameter parameter = function.parameters[0] with { singleValue = value, buffer = mutation == "buffer-conflict" ? function.parameters[0].buffer : null };
        function = function with { parameters = function.parameters.SetItem(0, parameter) };
        Assert.Throws<NotSupportedException>(() => new ValueParameterPlanner().Plan(module, function, parameter, Resolve, new Dictionary<StableId, CSharpStoragePlan>()));
    }

    private static string Resolve(StableId type) => type.value == "pointer" || type.value == "count-pointer" ? "int*" : "int";
    private static InteropModule ChangeCount(InteropModule module, InteropType type) => new(module.libraryId, module.targetId, module.pointerSize,
        module.types.SetItem(2, type), module.functions, module.diagnostics) { byteOrder = module.byteOrder };
    private static InteropModule ChangeElement(InteropModule module, InteropType type) => new(module.libraryId, module.targetId, module.pointerSize,
        module.types.SetItem(0, type), module.functions, module.diagnostics) { byteOrder = module.byteOrder };

    private static (InteropModule, InteropFunction) Module()
    {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:buffer");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType pointer = new(new("pointer"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer.id };
        InteropType count = integer with { id = new("count-value") };
        InteropType countPointer = pointer with { id = new("count-pointer"), elementType = count.id };
        InteropParameter buffer = new(new("buffer"), "buffer", pointer.id) { buffer = new(integer.id, NativeLengthUnit.Elements, NativeParameterDirection.Output,
            new(NativeLifetimeKind.Call, null, evidence), evidence) { capacityParameter = new("bound"), writtenCountParameter = new("count"), retainsPointer = false, allowsNull = true } };
        InteropFunction function = new(new("write"), "write", integer.id,
            [buffer, new(new("bound"), "capacity", integer.id), new(new("count"), "written", countPointer.id)], "C");
        return (new("buffer", "windows-x64-msvc", 8, [integer, pointer, count, countPointer], [function], []) { byteOrder = NativeByteOrder.LittleEndian }, function);
    }
}
