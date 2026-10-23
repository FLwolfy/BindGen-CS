using System;
using System.Collections.Immutable;
using BGCS.Analysis.Calls;
using BGCS.Core.Calls;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Interop.DotNet.Tests;

public sealed class DotNetProviderTests
{
    [Theory]
    [InlineData("dotnet.coreclr", false, true)]
    public void ExplicitRuntimeSelectionBindsExactFacts(
        string identity,
        bool staticThunks,
        bool blocking
    ) {
        NativeTargetDescriptor target = Target(identity == "dotnet.mono-wasm");
        Assert.True(new DotNetRuntimeProvider().TryResolve(new(new(identity), target), out ManagedRuntimeProfile? profile));
        Assert.Equal(target.targetId.value, profile!.nativeTargetId);
        Assert.Equal(target.triple, profile.nativeTriple);
        Assert.Equal(staticThunks, profile.requiresStaticCallbackThunks);
        Assert.Equal(blocking, profile.allowsBlockingDisposal);
    }

    [Fact]
    public void UnownedRuntimeDoesNotFallBackToDesktop()
    {
        Assert.False(new DotNetRuntimeProvider().TryResolve(new(new("private.execution"), Target(false)), out _));
        NativeTargetDescriptor unknown = new(new("private-target"), "private-platform", "x64", "msvc", "x86_64-pc-windows-msvc",
            new(), new(false, true), NativeByteOrder.LittleEndian);
        Assert.Throws<NotSupportedException>(() => new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), unknown), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardAndReverseScalarPlansBindCompleteSignatures(bool reverse)
    {
        NativeTargetDescriptor target = Target(false);
        ManagedRuntimeProfile runtime = Runtime(target);
        InteropType integer = Integer();
        InteropType callback = new(new("callback"), "int(int)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [integer.id], callingConvention = "C" };
        InteropFunction function = new(new("echo"), "echo", integer.id, [new(new("value"), "value", integer.id)], "C");
        InteropModule module = new("fixture", target.targetId.value, 8, [integer, callback], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallSignature signature = reverse ? NativeCallSignature.FromCallback(callback) : NativeCallSignature.FromFunction(function);
        CallAbiAssessment assessment = new DotNetCallAbiProvider().Assess(new(module, target, runtime, signature));
        Assert.Equal(NativeCallDisposition.Direct, assessment.disposition);
        CallPlanValidator validation = new(module, runtime);
        validation.Verify(assessment.plan!);
        Assert.Equal(signature.direction, assessment.plan!.signature.direction);
        Assert.Equal(integer.id, Assert.Single(assessment.plan.parameters).nativeType);
        Assert.Throws<InvalidOperationException>(() => validation.Verify(assessment.plan with
        { signature = signature with { parameterTypes = [] } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarAliasResolutionRetainsCycleAndIncompleteLinkRefusals(bool cycle)
    {
        NativeTargetDescriptor target = Target(false);
        ManagedRuntimeProfile runtime = Runtime(target);
        InteropType integer = Integer();
        InteropType alias = new(new("alias"), "Count", NativeTypeCategory.Alias, 4, 4) { elementType = integer.id };
        InteropType qualified = new(new("qualified"), "const Count", NativeTypeCategory.Qualified, 4, 4) { elementType = alias.id };
        InteropFunction function = new(new("echo"), "echo", qualified.id, [new(new("value"), "value", alias.id)], "C");
        InteropModule module = new("fixture", target.targetId.value, 8, [integer, alias, qualified], [function], [])
        { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallSignature signature = NativeCallSignature.FromFunction(function);
        DotNetCallAbiProvider provider = new();
        CallAbiAssessment healthy = provider.Assess(new(module, target, runtime, signature));
        Assert.Equal(NativeCallDisposition.Direct, healthy.disposition);
        new CallPlanValidator(module, runtime).Verify(healthy.plan!);
        InteropType invalid = alias with { elementType = cycle ? qualified.id : null };
        InteropModule broken = new(module.libraryId, module.targetId, module.pointerSize, [integer, invalid, qualified], [function], [])
        { byteOrder = module.byteOrder };
        CallAbiAssessment failure = provider.Assess(new(broken, target, runtime, signature));
        Assert.Equal(NativeCallDisposition.RequiresBridge, failure.disposition);
        Assert.Null(failure.plan);
        Assert.Contains("incomplete or cyclic aliases", failure.reason, StringComparison.Ordinal);
        Assert.Equal(NativeCallDisposition.Direct, provider.Assess(new(module, target, runtime, signature)).disposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpecialValueRefusalNamesTheNativeTypeAndKeepsTheCallingBoundary(bool reverse)
    {
        NativeTargetDescriptor target = Target(false);
        ManagedRuntimeProfile runtime = Runtime(target);
        InteropType scalar = new(new("long-double"), "long double", NativeTypeCategory.Primitive, 8, 8) { primitiveKind = "LongDouble" };
        InteropFunction function = new(new("extended"), "extended", scalar.id, [new(new("value"), "value", scalar.id)], "C");
        InteropType callback = new(new("extended-callback"), "long double(long double)", NativeTypeCategory.Function, null, null)
        { returnType = scalar.id, parameterTypes = [scalar.id], callingConvention = "C" };
        InteropModule module = new("fixture", target.targetId.value, 8, [scalar, callback], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallSignature signature = reverse ? NativeCallSignature.FromCallback(callback) : NativeCallSignature.FromFunction(function);
        CallAbiAssessment assessment = new DotNetCallAbiProvider().Assess(new(module, target, runtime, signature));
        Assert.Equal(NativeCallDisposition.RequiresBridge, assessment.disposition);
        Assert.Null(assessment.plan);
        Assert.Equal(scalar.id, assessment.rejectionType);
        Assert.Contains("long double", assessment.reason, StringComparison.Ordinal);
        Assert.Contains("calling-ABI", assessment.reason, StringComparison.Ordinal);
        Assert.Contains("pointer", assessment.reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedParameterIdentityDoesNotGetReplacedByTheUnrelatedReturnType(bool reverse)
    {
        NativeTargetDescriptor target = Target(false);
        ManagedRuntimeProfile runtime = Runtime(target);
        InteropType integer = Integer();
        InteropType unsupported = new(new("aligned"), "Aligned", NativeTypeCategory.Record, 16, 16)
        { recordKind = "Struct", fields = [new(new("field"), "value", integer.id, 0, 0, null)] };
        InteropFunction function = new(new("inspect"), "inspect", integer.id, [new(new("value"), "value", unsupported.id)], "C");
        InteropType callback = new(new("inspect-callback"), "int(Aligned)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [unsupported.id], callingConvention = "C" };
        InteropModule module = new("fixture", target.targetId.value, 8, [integer, unsupported, callback], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallSignature signature = reverse ? NativeCallSignature.FromCallback(callback) : NativeCallSignature.FromFunction(function);
        CallAbiAssessment assessment = new DotNetCallAbiProvider().Assess(new(module, target, runtime, signature));
        Assert.Equal(NativeCallDisposition.RequiresBridge, assessment.disposition);
        Assert.Null(assessment.plan);
        Assert.Equal(unsupported.id, assessment.rejectionType);
        Assert.NotEqual(integer.id, assessment.rejectionType);
    }

    [Theory]
    [InlineData("triple")]
    [InlineData("runtime")]
    [InlineData("target")]
    [InlineData("width")]
    [InlineData("blocking")]
    [InlineData("transport")]
    [InlineData("thunks")]
    public void MutatedExecutionCapabilitiesCannotReuseAPlan(string fault)
    {
        NativeTargetDescriptor target = Target(false);
        ManagedRuntimeProfile runtime = Runtime(target);
        InteropType integer = Integer();
        InteropFunction function = new(new("echo"), "echo", integer.id, [], "C");
        InteropModule module = new("fixture", target.targetId.value, 8, [integer], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallPlan plan = new DotNetCallAbiProvider().Assess(new(module, target, runtime, NativeCallSignature.FromFunction(function))).plan!;
        ManagedRuntimeProfile changed = fault switch
        {
            "triple" => runtime with { nativeTriple = "other-triple" },
            "runtime" => runtime with { runtimeId = new("other.runtime") },
            "target" => runtime with { nativeTargetId = "other-target" },
            "width" => runtime with { pointerSize = 4 },
            "blocking" => runtime with { allowsBlockingDisposal = !runtime.allowsBlockingDisposal },
            "transport" => runtime with { functionPointerTransport = ManagedFunctionPointerTransport.NativeInteger },
            _ => runtime with { requiresStaticCallbackThunks = !runtime.requiresStaticCallbackThunks }
        };
        if (fault is "target" or "width") Assert.Throws<NotSupportedException>(() => new CallPlanValidator(module, changed).Verify(plan));
        else Assert.Throws<InvalidOperationException>(() => new CallPlanValidator(module, changed).Verify(plan));
        Assert.NotEqual(NativeCallDisposition.Direct, new DotNetCallAbiProvider().Assess(new(module, target, changed, plan.signature)).disposition);
    }

    private static InteropType Integer() => new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
    private static ManagedRuntimeProfile Runtime(
        NativeTargetDescriptor target,
        string id = "dotnet.coreclr"
    ) {
        Assert.True(new DotNetRuntimeProvider().TryResolve(new(new(id), target), out ManagedRuntimeProfile? profile));
        return profile!;
    }
    private static NativeTargetDescriptor Target(bool wasm) => wasm
        ? new(new("emscripten-wasm32-emscripten"), "emscripten", "wasm32", "emscripten", "wasm32-unknown-emscripten", new(), new(true, false), NativeByteOrder.LittleEndian)
        : new(new("windows-x64-msvc"), "windows", "x64", "msvc", "x86_64-pc-windows-msvc", new(), new(false, true), NativeByteOrder.LittleEndian);
}
