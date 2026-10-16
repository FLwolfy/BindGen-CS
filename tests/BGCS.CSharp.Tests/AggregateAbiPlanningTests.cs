using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class AggregateAbiPlanningTests
{
    [Theory]
    [InlineData(ManagedAggregateCallingAbi.WindowsX64, "Integer")]
    [InlineData(ManagedAggregateCallingAbi.SystemVAmd64, "Sse")]
    public void CallingClassificationUsesRealFieldsAndRuntimeAlgorithm(ManagedAggregateCallingAbi family, string classification)
    {
        InteropType floating = new(new("float"), "float", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Float" };
        InteropType pair = new(new("pair"), "Pair", NativeTypeCategory.Record, 8, 4)
        { declarationName = "Pair", recordKind = "Struct", fields = [new(new("a"), "a", floating.id, 0, 0, null), new(new("b"), "b", floating.id, 4, 32, null)] };
        CSharpEmissionPlan plan = Plan([floating, pair], pair, family);
        CSharpAggregateCallPlan call = Assert.Single(plan.aggregateCalls);
        Assert.Equal(classification, Assert.Single(call.eightByteClasses));
        CSharpStoragePlan storage = Assert.Single(plan.storage);
        Assert.Equal(4, storage.packing);
        Assert.Equal(4, storage.managedAlignment);
    }

    [Fact]
    public void SystemVOverlappingFloatAndIntegerUnionMergesToIntegerClass()
    {
        InteropType floating = new(new("double"), "double", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "Double" };
        InteropType integer = new(new("integer"), "unsigned long long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "UnsignedLongLong", isSigned = false };
        InteropType union = new(new("number"), "Number", NativeTypeCategory.Record, 8, 8)
        { declarationName = "Number", recordKind = "Union", fields = [new(new("real"), "real", floating.id, 0, 0, null), new(new("bits"), "bits", integer.id, 0, 0, null)] };
        Assert.Equal("Integer", Assert.Single(Assert.Single(Plan([floating, integer, union], union, ManagedAggregateCallingAbi.SystemVAmd64).aggregateCalls).eightByteClasses));
    }

    [Fact]
    public void PackedUnalignedSystemVFieldUsesMemoryRatherThanPretendingRegisterCompatibility()
    {
        InteropType octet = new(new("octet"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType floating = new(new("double"), "double", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "Double" };
        InteropType packed = new(new("packed"), "Packed", NativeTypeCategory.Record, 9, 1)
        { declarationName = "Packed", recordKind = "Struct", fields = [new(new("prefix"), "prefix", octet.id, 0, 0, null), new(new("value"), "value", floating.id, 1, 8, null)] };
        CSharpEmissionPlan plan = Plan([octet, floating, packed], packed, ManagedAggregateCallingAbi.SystemVAmd64);
        Assert.Equal("Memory", Assert.Single(Assert.Single(plan.aggregateCalls).eightByteClasses));
        Assert.Equal(1, Assert.Single(plan.storage).managedAlignment);
    }

    [Theory]
    [InlineData("over-alignment")]
    [InlineData("overlap")]
    [InlineData("incomplete-array")]
    [InlineData("bitfield")]
    [InlineData("special-scalar")]
    public void EqualSizesDoNotGrantUnsupportedAggregateShapePermission(string mutation)
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType array = new(new("array"), "int[]", NativeTypeCategory.Array, 4, 4) { elementType = integer.id };
        if (mutation == "special-scalar") integer = integer with { primitiveKind = "Float16" };
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 8, mutation == "over-alignment" ? 8 : 4)
        {
            declarationName = "Record", recordKind = "Struct",
            fields = [new(new("a"), "a", mutation == "incomplete-array" ? array.id : integer.id, 0, 0, mutation == "bitfield" ? 4 : null),
                new(new("b"), "b", integer.id, mutation == "overlap" ? 0 : 4, mutation == "overlap" ? 0 : 32, null)]
        };
        Assert.Throws<CSharpPlanningException>(() => Plan([integer, array, record], record, ManagedAggregateCallingAbi.WindowsX64));
    }

    [Fact]
    public void AProfileForAnotherTargetCannotAdmitAggregateCalls()
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Record", recordKind = "Struct", fields = [new(new("a"), "a", integer.id, 0, 0, null)] };
        InteropModule module = new("fixture", "windows-x64-msvc", 8, [integer, record], [new(new("echo"), "echo", record.id, [], "C")], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Aggregates", "AggregateApi"), CancellationToken.None);
        Assert.Throws<CSharpPlanningException>(() => new CSharpEmissionPlanner().Plan(model, new("fixture", new NullSink())
        { managedRuntime = Runtime(ManagedAggregateCallingAbi.SystemVAmd64) }));
    }

    private static CSharpEmissionPlan Plan(ImmutableArray<InteropType> types, InteropType record, ManagedAggregateCallingAbi family)
    {
        ManagedRuntimeProfile runtime = Runtime(family);
        InteropModule module = new("fixture", runtime.aggregateAbi!.nativeTargetId, 8, types,
            [new(new("echo"), "echo", record.id, [new(new("value"), "value", record.id)], "C")], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Aggregates", "AggregateApi"), CancellationToken.None);
        return new CSharpEmissionPlanner().Plan(model, new("fixture", new NullSink()) { managedRuntime = runtime });
    }

    private static ManagedRuntimeProfile Runtime(ManagedAggregateCallingAbi family)
    {
        string target = family == ManagedAggregateCallingAbi.WindowsX64 ? "windows-x64-msvc" : "linux-x64-gnu";
        return new("fixture", ManagedRuntimeKind.CoreClr, NativeModuleMechanism.DynamicLibrary, [BindingImportMode.FunctionTable], false, true)
        { aggregateAbi = new(target, family == ManagedAggregateCallingAbi.WindowsX64 ? "x86_64-pc-windows-msvc" : "x86_64-unknown-linux-gnu", 8, NativeByteOrder.LittleEndian, family) };
    }

    private sealed class NullSink : IArtifactSink
    {
        public string WriteText(string relativePath, string content) => relativePath;
    }
}
