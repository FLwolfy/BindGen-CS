using System;
using System.Collections.Generic;
using BGCS.Analysis.Calls;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Analysis.Tests;

public sealed class CallPlanIdentityLookupTests
{
    [Fact]
    public void ExactFunctionLookupStillValidatesTheCompleteSignatureRatherThanArrayPosition()
    {
        var (module, runtime, plan) = Fixture();
        CallPlanValidator validation = new(module, runtime);
        validation.Verify(plan);
        Assert.Equal(new StableId("other"), module.functions[0].id);
        Assert.Throws<InvalidOperationException>(() => validation.Verify(plan with
        { signature = plan.signature with { id = new("other") } }));
    }

    [Fact]
    public void APlanForAnAbsentFunctionRetainsAnExplicitInvalidPlanFailure()
    {
        var (module, runtime, plan) = Fixture();
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new CallPlanValidator(module, runtime).Verify(plan with
        { signature = plan.signature with { id = new("missing") } }));
        Assert.Contains("missing physical function identity", failure.Message, StringComparison.Ordinal);
        Assert.IsType<KeyNotFoundException>(failure.InnerException);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("size")]
    [InlineData("alignment")]
    public void IndexedFunctionResolutionCannotBypassValueStorageChecks(string mutation)
    {
        var (module, runtime, plan) = Fixture();
        NativeCallValuePlan original = plan.parameters[0];
        NativeCallValuePlan changed = mutation switch
        {
            "type" => original with { nativeType = new("other-type") },
            "size" => original with { size = 8 },
            _ => original with { alignment = 8 }
        };
        CallPlanValidator validation = new(module, runtime);
        Assert.Throws<InvalidOperationException>(() => validation.Verify(plan with { parameters = [changed] }));
        validation.Verify(plan);
    }

    [Fact]
    public void BoundValidationRetainsItsExactProfileWhileCopiedInputsRequireIndependentValidation()
    {
        var (module, runtime, plan) = Fixture();
        CallPlanValidator original = new(module, runtime);
        original.Verify(plan);
        ManagedRuntimeProfile changed = runtime with { allowsBlockingDisposal = !runtime.allowsBlockingDisposal };
        CallPlanValidator other = new(module, changed);
        Assert.Throws<InvalidOperationException>(() => other.Verify(plan));
        NativeCallPlan changedPlan = plan with { runtimeProfileDigest = ManagedRuntimeProfileIdentity.Capture(changed) };
        Assert.Throws<InvalidOperationException>(() => original.Verify(changedPlan));
        other.Verify(changedPlan);
        original.Verify(plan);
        InteropFunction function = module.GetFunction(plan.signature.id);
        InteropModule changedModule = new(module.libraryId, module.targetId, module.pointerSize, module.types,
            [function with { parameters = [] }], []) { byteOrder = module.byteOrder };
        Assert.Throws<InvalidOperationException>(() => new CallPlanValidator(changedModule, runtime).Verify(plan));
        original.Verify(plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarAliasResolutionStillRejectsCyclesAndIncompleteLinksWithoutPoisoningHealthyValidation(bool cycle)
    {
        var (source, runtime, originalPlan) = Fixture();
        InteropType alias = new(new("alias"), "Count", NativeTypeCategory.Alias, 4, 4) { elementType = new("integer") };
        InteropType qualified = new(new("qualified"), "const Count", NativeTypeCategory.Qualified, 4, 4) { elementType = alias.id };
        InteropFunction original = source.GetFunction(originalPlan.signature.id);
        InteropFunction function = original with { returnType = qualified.id, parameters = [original.parameters[0] with { type = alias.id }] };
        InteropModule module = new(source.libraryId, source.targetId, source.pointerSize, source.types.Add(alias).Add(qualified), [function], [])
        { byteOrder = source.byteOrder };
        NativeCallPlan plan = originalPlan with
        {
            signature = NativeCallSignature.FromFunction(function), returnValue = originalPlan.returnValue! with { nativeType = qualified.id },
            parameters = [originalPlan.parameters[0] with { nativeType = alias.id }]
        };
        CallPlanValidator validation = new(module, runtime);
        validation.Verify(plan);
        InteropType invalid = alias with { elementType = cycle ? qualified.id : null };
        InteropModule broken = new(module.libraryId, module.targetId, module.pointerSize, source.types.Add(invalid).Add(qualified), [function], [])
        { byteOrder = module.byteOrder };
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => new CallPlanValidator(broken, runtime).Verify(plan));
        Assert.Contains("cyclic or incomplete aliases", failure.Message, StringComparison.Ordinal);
        validation.Verify(plan);
    }

    private static (InteropModule module, ManagedRuntimeProfile runtime, NativeCallPlan plan) Fixture()
    {
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropFunction expected = new(new("expected"), "native_expected", integer.id, [new(new("argument"), "argument", integer.id)], "C");
        InteropFunction other = new(new("other"), "native_other", integer.id, [], "C");
        InteropModule module = new("fixture", "fixture-target", 8, [integer], [other, expected], []) { byteOrder = NativeByteOrder.LittleEndian };
        ManagedRuntimeProfile runtime = new("fixture-profile", new("fixture.runtime"), NativeModuleMechanism.ExternalFunctionTable,
            [BindingImportMode.FunctionTable], false, true)
        { nativeTargetId = module.targetId, nativeTriple = "fixture-triple", pointerSize = 8, byteOrder = module.byteOrder, callAbi = new("fixture.abi") };
        NativeCallValuePlan value = new(integer.id, 4, 4, ["Integer"]);
        NativeCallPlan plan = new(NativeCallSignature.FromFunction(expected), module.targetId, runtime.runtimeId, runtime.id,
            runtime.callAbi, module.pointerSize, module.byteOrder, value, [value])
        { nativeTriple = runtime.nativeTriple, runtimeProfileDigest = ManagedRuntimeProfileIdentity.Capture(runtime) };
        return (module, runtime, plan);
    }
}
