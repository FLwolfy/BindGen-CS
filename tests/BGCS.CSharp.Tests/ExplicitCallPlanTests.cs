using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Calls;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ExplicitCallPlanTests
{
    [Fact]
    public void PublicPlannerRequiresRuntimeEvenForSimpleScalarFunctions()
    {
        var model = NeutralBackendTests.Model();
        Assert.Throws<NotSupportedException>(() => new CSharpEmissionPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())));
    }

    [Fact]
    public void PublicPlannerRequiresAnExactPlanEvenForSimpleScalarFunctions()
    {
        var model = NeutralBackendTests.Model();
        EmissionRequest request = PlanningFixture.Prepare(model, new("native", new NeutralBackendTests.MemorySink()));
        Assert.Throws<CSharpPlanningException>(() => new CSharpEmissionPlanner().Plan(model, request with { callPlans = [] }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicPlannerRejectsContradictoryNativeAndGeneratedOwnerIdentities(bool typeOwner)
    {
        var model = NeutralBackendTests.ModelWithStorage();
        var source = model.interop;
        if (typeOwner)
        {
            var original = source.types.Single(type => type.declarationName == "Outer");
            var changed = original with { sourceNativeType = new("original-owner"), sourceGeneratedType = new("generated-owner") };
            model = model with { interop = new BGCS.Core.Interop.InteropModule(source.libraryId, source.targetId, source.pointerSize, source.types.Replace(original, changed), source.functions, source.diagnostics) { byteOrder = source.byteOrder } };
        }
        else
        {
            var original = source.functions[0];
            var changed = original with { sourceOwnerType = new("original-owner"), sourceGeneratedOwnerType = new("generated-owner") };
            model = model with { interop = new BGCS.Core.Interop.InteropModule(source.libraryId, source.targetId, source.pointerSize, source.types, source.functions.Replace(original, changed), source.diagnostics) { byteOrder = source.byteOrder } };
        }
        var failure = Assert.Throws<ArgumentException>(() => new CSharpEmissionPlanner().Plan(model,
            PlanningFixture.Prepare(model, new("native", new NeutralBackendTests.MemorySink()))));
        Assert.Contains("owner identities", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("triple")]
    [InlineData("direction")]
    [InlineData("return")]
    [InlineData("parameters")]
    [InlineData("convention")]
    [InlineData("prototype")]
    [InlineData("runtime-digest")]
    public void PublicPlannerRejectsStaleOrMutatedPhysicalCallPlans(string fault)
    {
        var model = NeutralBackendTests.Model();
        EmissionRequest request = PlanningFixture.Prepare(model, new("native", new NeutralBackendTests.MemorySink()));
        NativeCallPlan original = Assert.Single(request.callPlans, plan => plan.signature.id.value == "add");
        NativeCallPlan plan = original;
        NativeCallSignature signature = plan.signature;
        plan = fault switch
        {
            "triple" => plan with { nativeTriple = "another-triple" },
            "direction" => plan with { signature = signature with { direction = (NativeCallDirection)123 } },
            "return" => plan with { returnValue = null },
            "parameters" => plan with { parameters = [] },
            "convention" => plan with { signature = signature with { callingConvention = "Stdcall" } },
            "prototype" => plan with { signature = signature with { hasPrototype = false } },
            _ => plan with { runtimeProfileDigest = default }
        };
        Assert.Throws<InvalidOperationException>(() => new CSharpEmissionPlanner().Plan(model, request with { callPlans = request.callPlans.Replace(original, plan) }));
    }
}
