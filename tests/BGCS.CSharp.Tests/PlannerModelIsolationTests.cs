using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class PlannerModelIsolationTests
{
    [Theory]
    [InlineData("missing-native-export")]
    [InlineData("duplicate-native-export")]
    [InlineData("duplicate-managed-export")]
    public void AmbiguousOrMissingIdentityCannotProduceAnImportPlanOrPoisonTheNextRequest(string fault)
    {
        ManagedApiModel original = Model(false);
        CSharpTestPlanner planner = new();
        NeutralBackendTests.MemorySink sink = new();
        if (fault == "duplicate-native-export")
        {
            InteropModule module = original.interop;
            Assert.Throws<ArgumentException>(() => new InteropModule(module.libraryId, module.targetId, module.pointerSize, module.types,
                module.functions.Add(module.functions[0]), []));
            Assert.Empty(sink.sources);
            AssertPlan(planner.Plan(original, new("fixture", sink)), original, replacement: false);
            return;
        }
        ManagedApiModel invalid = fault == "missing-native-export"
            ? original with { methods = original.methods.SetItem(0, original.methods[0] with { nativeExport = new("missing") }) }
            : original with { methods = original.methods.SetItem(1, original.methods[1] with { nativeExport = original.methods[0].nativeExport }) };
        Exception? failure = Record.Exception(() => planner.Plan(invalid, new("fixture", sink)));
        Assert.True(failure is ArgumentException or InvalidOperationException, failure?.ToString() ?? "The invalid identity unexpectedly produced a plan.");
        Assert.Empty(sink.sources);
        AssertPlan(planner.Plan(original, new("fixture", sink)), original, replacement: false);
    }

    [Fact]
    public void ReplacingImmutableModelsWithTheSameIdsUsesTheirOwnOrderCarriersNamesAndDefaultValues()
    {
        ManagedApiModel original = Model(false);
        ManagedApiModel different = Model(true);
        ManagedApiModel replacement = original with { interop = different.interop, methods = different.methods };
        CSharpTestPlanner planner = new();
        AssertPlan(planner.Plan(original, Request()), original, replacement: false);
        AssertPlan(planner.Plan(replacement, Request()), replacement, replacement: true);
        AssertPlan(planner.Plan(original, Request()), original, replacement: false);
        Assert.Equal("read_original", original.interop.functions[0].exportName);
        Assert.Equal("7", original.methods[0].defaults[0].value.value);
    }

    [Fact]
    public async Task ConcurrentPlansForReplacedModelsCannotExchangeNativeSignaturesOrConvenienceDefaults()
    {
        ManagedApiModel original = Model(false), different = Model(true);
        ManagedApiModel replacement = original with { interop = different.interop, methods = different.methods };
        CSharpTestPlanner planner = new();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
        {
            bool changed = index % 2 != 0;
            ManagedApiModel model = changed ? replacement : original;
            AssertPlan(planner.Plan(model, Request()), model, changed);
        })));
    }

    private static void AssertPlan(CSharpEmissionPlan plan, ManagedApiModel model, bool replacement)
    {
        Assert.Same(model, plan.api);
        Assert.Equal(model.methods.Select(member => member.nativeExport), plan.methods.Select(method => method.nativeExport));
        CSharpMethodPlan read = Assert.Single(plan.methods, method => method.nativeExport == new StableId("read"));
        string type = replacement ? "long" : "int", name = replacement ? "ReadReplacement" : "ReadOriginal";
        Assert.Equal(replacement ? "read_replacement" : "read_original", read.exportName);
        Assert.Equal(type, read.returnType);
        Assert.Equal(type, Assert.Single(read.parameterTypes));
        Assert.Equal(name, read.name);
        CSharpDefaultOverloadPlan omitted = Assert.Single(plan.defaultOverloads);
        Assert.Equal(name, omitted.signature.name);
        Assert.Equal(type, omitted.signature.returnType);
        Assert.Empty(omitted.signature.parameterTypes);
        Assert.Equal(replacement ? "(long)(17L)" : "(int)(7)", Assert.Single(omitted.arguments));
        Assert.NotNull(plan.sharedContext);
        Assert.Contains(plan.sharedContext.methods, method => method.nativeExport == read.nativeExport && method.name == name
            && method.returnType == type && method.parameterTypes.Length == 0);
    }

    private static EmissionRequest Request() => new("fixture", new NeutralBackendTests.MemorySink());
    private static ManagedApiModel Model(bool replacement)
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType wide = new(new("wide"), "long long", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "LongLong", isSigned = true };
        StableId scalar = replacement ? wide.id : integer.id;
        InteropFunction read = new(new("read"), replacement ? "read_replacement" : "read_original", scalar,
            [new(new("read-argument"), "value", scalar)], "C");
        InteropFunction other = new(new("other"), "other", integer.id, [new(new("other-argument"), "other", integer.id)], "C");
        InteropModule module = new("isolation", "windows-x64-msvc", 8, [integer, wide], replacement ? [other, read] : [read, other], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        ManagedApiModel model = new DefaultManagedProjection().Project(module, new("Generated.PlannerIsolation", "Api")
        { style = ManagedApiStyle.Static }, CancellationToken.None);
        return model with { methods = model.methods.Select(member => member.nativeExport == read.id ? member with
        {
            name = replacement ? "ReadReplacement" : "ReadOriginal",
            defaults = [new(new("read-argument"), new(ManagedDefaultValueKind.Integer, replacement ? "17" : "7"))]
        } : member with { name = "Other" }).ToImmutableArray() };
    }
}
