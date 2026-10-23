using System;
using BGCS.Analysis.Calls;
using BGCS.Core.Calls;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Extension.NativeAot.Tests;

public sealed class NativeAotProviderTests
{
    [Fact]
    public void RuntimeIsExplicitAndBindsStaticReverseThunksWithoutChangingTargetFacts()
    {
        NativeAotRuntimeProvider provider = new();
        Assert.False(provider.TryResolve(new(default, Target()), out _));
        Assert.True(provider.TryResolve(new(new("dotnet.native-aot"), Target()), out ManagedRuntimeProfile? runtime));
        Assert.Equal("dotnet.native-aot", runtime!.runtimeId.value);
        Assert.True(runtime.requiresStaticCallbackThunks);
        Assert.True(runtime.allowsBlockingDisposal);
        Assert.Equal(Target().triple, runtime.nativeTriple);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectPlanBindsForwardAndReverseFullSignatures(bool reverse)
    {
        NativeTargetDescriptor target = Target();
        Assert.True(new NativeAotRuntimeProvider().TryResolve(new(new("dotnet.native-aot"), target), out ManagedRuntimeProfile? runtime));
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType callback = new(new("callback"), "int(int)", NativeTypeCategory.Function, null, null)
        { returnType = integer.id, parameterTypes = [integer.id], callingConvention = "C" };
        InteropFunction echo = new(new("echo"), "echo", integer.id, [new(new("value"), "value", integer.id)], "C");
        InteropModule module = new("fixture", target.targetId.value, 8, [integer, callback], [echo], []) { byteOrder = NativeByteOrder.LittleEndian };
        NativeCallSignature signature = reverse ? NativeCallSignature.FromCallback(callback) : NativeCallSignature.FromFunction(echo);
        CallAbiAssessment assessment = new NativeAotCallAbiProvider().Assess(new(module, target, runtime!, signature));
        Assert.Equal(NativeCallDisposition.Direct, assessment.disposition);
        new CallPlanValidator(module, runtime!).Verify(assessment.plan!);
        Assert.Equal(target.triple, assessment.plan!.nativeTriple);
        Assert.Equal(signature.direction, assessment.plan.signature.direction);
        Assert.Throws<InvalidOperationException>(() => new CallPlanValidator(module,
            runtime! with { requiresStaticCallbackThunks = false }).Verify(assessment.plan));
        Assert.NotEqual(NativeCallDisposition.Direct, new NativeAotCallAbiProvider().Assess(new(module, target,
            runtime! with { requiresStaticCallbackThunks = false }, signature)).disposition);
    }

    private static NativeTargetDescriptor Target() => new(new("linux-x64-gnu"), "linux", "x64", "gnu", "x86_64-unknown-linux-gnu",
        new(), new(false, true), NativeByteOrder.LittleEndian);
}
