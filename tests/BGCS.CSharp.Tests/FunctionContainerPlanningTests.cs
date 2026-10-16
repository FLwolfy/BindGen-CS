using System;
using System.Linq;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class FunctionContainerPlanningTests
{
    [Fact]
    public void EmptyContainersPreserveOperationsAndStillRejectUninitializedCollections()
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlanner planner = new();
        CSharpEmissionPlan planned = planner.Plan(model, new("native", sink));
        Assert.Empty(planned.functionContainers);
        Assert.Equal(model.methods.Select(method => method.nativeExport), planned.methods.Select(method => method.nativeExport));
        Assert.Throws<ArgumentException>(() => planner.Plan(model with { functionContainers = default }, new("native", sink)));
        Assert.Throws<ArgumentException>(() => planner.Plan(model with
        {
            methods = model.methods.SetItem(0, model.methods[0] with { nativeExport = new("missing-native-operation") })
        }, new("native", sink)));
        Assert.Empty(sink.sources);
    }

    [Fact]
    public void ConfiguredContainersStillResolveTheCheckedOriginalSignature()
    {
        ManagedApiModel original = NeutralBackendTests.Model();
        ManagedApiModel model = original with { functionContainers = [new("Math", [new(original.methods[0].nativeExport, "Plus")])] };
        CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        CSharpContainerMethodPlan operation = Assert.Single(Assert.Single(plan.functionContainers).methods);
        Assert.Equal(plan.methods[0].name, operation.targetName);
        Assert.Equal("Plus", operation.signature.name);
        Assert.Equal(plan.methods[0].nativeExport, operation.signature.nativeExport);
    }

    [Fact]
    public void ConfiguredContainersRetainUnknownOperationAndDuplicateViewFailures()
    {
        ManagedApiModel model = NeutralBackendTests.Model();
        NeutralBackendTests.MemorySink sink = new();
        CSharpEmissionPlanner planner = new();
        Assert.Throws<NotSupportedException>(() => planner.Plan(model with
        { functionContainers = [new("Math", [new(new("missing-native-operation"), "Plus")])] }, new("native", sink)));
        ManagedFunctionContainer container = new("Math", [new(model.methods[0].nativeExport, "Plus")]);
        Assert.Throws<ArgumentException>(() => planner.Plan(model with { functionContainers = [container, container] }, new("native", sink)));
        Assert.Empty(sink.sources);
    }
}
