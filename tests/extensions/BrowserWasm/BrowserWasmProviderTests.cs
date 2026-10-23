using System;
using BGCS.Analysis.Calls;
using BGCS.Core.Calls;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Extension.BrowserWasm.Tests;

public sealed class BrowserWasmProviderTests
{
    [Fact]
    public void BrowserRuntimeCannotBeImplicitOrAppliedToAnotherExecutionTarget()
    {
        BrowserWasmRuntimeProvider provider = new();
        Assert.False(provider.TryResolve(new(default, Target()), out _));
        Assert.True(provider.TryResolve(new(new("dotnet.mono-wasm"), Target()), out ManagedRuntimeProfile? runtime));
        Assert.True(runtime!.requiresStaticCallbackThunks);
        Assert.False(runtime.allowsBlockingDisposal);
        Assert.Equal(ManagedFunctionPointerTransport.NativeInteger, runtime.functionPointerTransport);
        NativeTargetDescriptor other = new(new("other"), "private", "wasm32", "emscripten", Target().triple,
            new(), new(true, false), NativeByteOrder.LittleEndian);
        Assert.Throws<NotSupportedException>(() => provider.TryResolve(new(new("dotnet.mono-wasm"), other), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarPlansBindCompleteForwardAndReverseSignatures(bool reverse)
    {
        NativeTargetDescriptor target = Target();
        ManagedRuntimeProfile runtime = Runtime();
        InteropType integer = Integer();
        InteropType callback = new(new("callback"), "int(int)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [integer.id], callingConvention = "C" };
        InteropFunction function = new(new("echo"), "echo", integer.id, [new(new("value"), "value", integer.id)], "C");
        InteropModule module = new("fixture", target.targetId.value, 4, [integer, callback], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallSignature signature = reverse ? NativeCallSignature.FromCallback(callback) : NativeCallSignature.FromFunction(function);
        CallAbiAssessment assessment = new BrowserWasmCallAbiProvider().Assess(new(module, target, runtime, signature));
        Assert.Equal(NativeCallDisposition.Direct, assessment.disposition);
        CallPlanValidator validation = new(module, runtime);
        validation.Verify(assessment.plan!);
        Assert.Equal(target.triple, assessment.plan!.nativeTriple);
        Assert.Throws<InvalidOperationException>(() => validation.Verify(
            assessment.plan with { signature = signature with { direction = (NativeCallDirection)123 } }));
        Assert.NotEqual(NativeCallDisposition.Direct, new BrowserWasmCallAbiProvider().Assess(new(module, target,
            runtime with { allowsBlockingDisposal = true }, signature)).disposition);
    }

    [Fact]
    public void EqualSizeAggregateStillRequiresPointerBridge()
    {
        InteropType integer = Integer();
        InteropType record = new(new("record"), "Record", NativeTypeCategory.Record, 4, 4)
        { recordKind = "Struct", fields = [new(new("field"), "value", integer.id, 0, 0, null)] };
        InteropFunction function = new(new("echo"), "echo", record.id, [new(new("value"), "value", record.id)], "C");
        InteropModule module = new("fixture", Target().targetId.value, 4, [integer, record], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        CallAbiAssessment assessment = new BrowserWasmCallAbiProvider().Assess(new(module, Target(), Runtime(), NativeCallSignature.FromFunction(function)));
        Assert.Equal(NativeCallDisposition.RequiresBridge, assessment.disposition);
        Assert.Null(assessment.plan);
        Assert.Contains("pointer", assessment.reason, StringComparison.OrdinalIgnoreCase);
    }

    private static InteropType Integer() => new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
    private static ManagedRuntimeProfile Runtime()
    {
        Assert.True(new BrowserWasmRuntimeProvider().TryResolve(new(new("dotnet.mono-wasm"), Target()), out ManagedRuntimeProfile? runtime));
        return runtime!;
    }
    private static NativeTargetDescriptor Target() => new(new("emscripten-wasm32-emscripten"), "emscripten", "wasm32", "emscripten", "wasm32-unknown-emscripten",
        new(), new(true, false), NativeByteOrder.LittleEndian);
}
