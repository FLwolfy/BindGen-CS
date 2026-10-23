using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Artifacts;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.Interop.DotNet.Tests;

public sealed class AggregateAbiPlanningTests
{
    [Theory]
    [InlineData("dotnet.windows-x64", "Integer")]
    [InlineData("dotnet.system-v-amd64", "Sse")]
    public void CallingClassificationUsesRealFieldsAndRuntimeAlgorithm(
        string family,
        string classification
    ) {
        InteropType floating = new(new("float"), "float", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Float" };
        InteropType pair = new(new("pair"), "Pair", NativeTypeCategory.Record, 8, 4)
        { declarationName = "Pair", recordKind = "Struct", fields = [new(new("a"), "a", floating.id, 0, 0, null), new(new("b"), "b", floating.id, 4, 32, null)] };
        CSharpEmissionPlan plan = Plan([floating, pair], pair, family);
        CSharpAggregateCallPlan call = Assert.Single(plan.aggregateCalls);
        Assert.Equal(classification, Assert.Single(Assert.Single(call.callSites).parameters[0].classes));
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
        Assert.Equal("Integer", Assert.Single(Assert.Single(Plan([floating, integer, union], union, "dotnet.system-v-amd64").aggregateCalls).callSites[0].parameters[0].classes));
    }

    [Fact]
    public void PackedUnalignedSystemVFieldUsesMemoryRatherThanPretendingRegisterCompatibility()
    {
        InteropType octet = new(new("octet"), "unsigned char", NativeTypeCategory.Primitive, 1, 1) { primitiveKind = "UnsignedChar", isSigned = false };
        InteropType floating = new(new("double"), "double", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "Double" };
        InteropType packed = new(new("packed"), "Packed", NativeTypeCategory.Record, 9, 1)
        { declarationName = "Packed", recordKind = "Struct", fields = [new(new("prefix"), "prefix", octet.id, 0, 0, null), new(new("value"), "value", floating.id, 1, 8, null)] };
        CSharpEmissionPlan plan = Plan([octet, floating, packed], packed, "dotnet.system-v-amd64");
        Assert.Equal("Memory", Assert.Single(Assert.Single(plan.aggregateCalls).callSites[0].parameters[0].classes));
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
            declarationName = "Record",
            recordKind = "Struct",
            fields = [new(new("a"), "a", mutation == "incomplete-array" ? array.id : integer.id, 0, 0, mutation == "bitfield" ? 4 : null),
                new(new("b"), "b", integer.id, mutation == "overlap" ? 0 : 4, mutation == "overlap" ? 0 : 32, null)]
        };
        Assert.Throws<CSharpPlanningException>(() => Plan([integer, array, record], record, "dotnet.windows-x64"));
    }

    [Fact]
    public void AProfileForAnotherTargetCannotAdmitAggregateCalls()
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 4, 4)
        { declarationName = "Record", recordKind = "Struct", fields = [new(new("a"), "a", integer.id, 0, 0, null)] };
        InteropModule module = new("fixture", "windows-x64-msvc", 8, [integer, record], [new(new("echo"), "echo", record.id, [], "C")], []) { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Aggregates", "AggregateApi"), CancellationToken.None);
        Assert.Throws<NotSupportedException>(() => new CSharpEmissionPlanner().Plan(model, new("fixture", new NullSink())
        { managedRuntime = Runtime("dotnet.system-v-amd64") }));
    }

    private static CSharpEmissionPlan Plan(
        ImmutableArray<InteropType> types,
        InteropType record,
        string family
    ) {
        ManagedRuntimeProfile runtime = Runtime(family);
        InteropModule module = new("fixture", runtime.nativeTargetId, 8, types,
            [new(new("echo"), "echo", record.id, [new(new("value"), "value", record.id)], "C")], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        var model = new DefaultManagedProjection().Project(module, new("Generated.Aggregates", "AggregateApi"), CancellationToken.None);
        NativeTargetDescriptor target = Target(family);
        CallAbiAssessment assessment = new DotNetCallAbiProvider().Assess(new(module, target, runtime, NativeCallSignature.FromFunction(module.functions[0])));
        return new CSharpEmissionPlanner().Plan(model, new("fixture", new NullSink())
        { managedRuntime = runtime, callPlans = assessment.plan is null ? [] : [assessment.plan] });
    }

    private static ManagedRuntimeProfile Runtime(string family)
    {
        Assert.True(new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), Target(family)), out ManagedRuntimeProfile? profile));
        return profile!;
    }

    private static NativeTargetDescriptor Target(string family)
        => family == "dotnet.windows-x64"
            ? new(new("windows-x64-msvc"), "windows", "x64", "msvc", "x86_64-pc-windows-msvc", new(), new(false, true), NativeByteOrder.LittleEndian)
            : new(new("linux-x64-gnu"), "linux", "x64", "gnu", "x86_64-unknown-linux-gnu", new(), new(false, true), NativeByteOrder.LittleEndian);

    private sealed class NullSink : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => relativePath;
    }
}
