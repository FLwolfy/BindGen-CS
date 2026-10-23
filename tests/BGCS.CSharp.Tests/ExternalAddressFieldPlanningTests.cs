using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BGCS.Analysis.Layout;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ExternalAddressFieldPlanningTests
{
    [Theory]
    [InlineData("System.Int32*")]
    [InlineData("System.IntPtr")]
    [InlineData("System.UIntPtr")]
    public void AddressFieldsRetainTheirExactCopyRoleWithoutGrantingPointeeLifetime(string representation)
    {
        (InteropModule module, ExternalTypeProjection projection) = Create(representation);
        CSharpExternalTypeProjectionPlan plan = new ExternalCarrierPlanner().Plan(module, projection, "RawAddress",
            new Dictionary<StableId, string> { [new("data-field")] = "data" });
        Assert.True(Assert.Single(plan.fields).isNativeAddress);
        Assert.Equal("Data", plan.fields[0].managedField);
        Assert.Equal("RawAddress", plan.rawCarrierType);
        Assert.DoesNotContain(typeof(CSharpExternalFieldCopyPlan).GetProperties(), property => property.Name.Contains("Ownership", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("integer-kind")]
    [InlineData("integer-type")]
    [InlineData("pointer-width")]
    [InlineData("native-width")]
    [InlineData("byte-order")]
    [InlineData("unknown-byte-order")]
    [InlineData("function-pointer")]
    [InlineData("unknown-pointee")]
    [InlineData("managed-overflow")]
    [InlineData("native-overflow")]
    [InlineData("native-overrun")]
    public void EqualSizeIntegersAndUnprovenAddressStorageCannotAcquirePointerSemantics(string fault)
    {
        (InteropModule module, ExternalTypeProjection projection) = Create("System.IntPtr");
        ManagedTypeStorageFact fact = projection.managedStorage!;
        switch (fault)
        {
            case "integer-kind": fact = fact with { fields = [fact.fields[0] with { scalarKind = ManagedScalarKind.SignedInteger }] }; break;
            case "integer-type": fact = fact with { fields = [fact.fields[0] with { typeIdentity = "System.Int64" }] }; break;
            case "pointer-width": fact = fact with { probe = fact.probe with { pointerSize = 4 } }; break;
            case "byte-order": fact = fact with { probe = fact.probe with { isLittleEndian = false } }; break;
            case "unknown-byte-order": module = Rebuild(module, module.types, NativeByteOrder.Unknown); break;
            case "native-width": module = Rebuild(module, module.types.SetItem(1, module.types[1] with { size = 4 })); break;
            case "function-pointer":
                module = Rebuild(module, module.types.SetItem(0,
                module.types[0] with { category = NativeTypeCategory.Function, returnType = module.types[0].id })); break;
            case "unknown-pointee": module = Rebuild(module, module.types.SetItem(1, module.types[1] with { elementType = null })); break;
            case "managed-overflow": fact = fact with { fields = [fact.fields[0] with { offset = long.MaxValue - 1 }] }; break;
            case "native-overflow":
                module = Rebuild(module, module.types.SetItem(2,
                module.types[2] with { fields = [module.types[2].fields[0] with { offset = long.MaxValue - 1 }] })); break;
            case "native-overrun":
                module = Rebuild(module, module.types.SetItem(2,
                module.types[2] with { fields = [module.types[2].fields[0] with { offset = 1 }] })); break;
        }
        projection = projection with { managedStorage = fact };
        Assert.ThrowsAny<Exception>(() => new ExternalTypeContractValidator().ValidateProjection(module, projection));
    }

    private static InteropModule Rebuild(
        InteropModule module,
        ImmutableArray<InteropType> types,
        NativeByteOrder? byteOrder = null
    )
        => new(module.libraryId, module.targetId, module.pointerSize, types, [], []) { byteOrder = byteOrder ?? module.byteOrder };

    private static (InteropModule, ExternalTypeProjection) Create(string representation)
    {
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType pointer = new(new("pointer"), "int*", NativeTypeCategory.Pointer, 8, 8) { elementType = integer.id };
        InteropType record = new(new("record"), "Address", NativeTypeCategory.Record, 8, 8)
        { recordKind = "Struct", fields = [new(new("data-field"), "data", pointer.id, 0, 0, null)] };
        InteropModule module = new("address-copy", "windows-x64-msvc", 8, [integer, pointer, record], [], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        ManagedProbeIdentity identity = new(".NET 10.0.12", "win-x64", "X64", 8, true, new(new string('1', 64)));
        ManagedTypeStorageFact fact = new("Consumer.Address", "Consumer.Address", 8, 8, true, "Sequential",
            [new("Data", representation, ManagedScalarKind.NativeAddress, 8, 0, true, true)], identity,
            new(EvidenceLevel.Verified, "bgcs:managed-field-probe:v1"));
        return (module, new(record.id, fact.managedType, [new(new("data-field"), "Data")]) { managedStorage = fact });
    }
}
