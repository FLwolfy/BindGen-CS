using System;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Analysis.Calls;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using Xunit;

namespace BGCS.Interop.DotNet.Tests;

public sealed class DotNetDerivedProfileTests
{
    [Fact]
    public void SharedProfileFactsNeverReuseModuleSignatureOrCarrierAssessments()
    {
        CallAbiRequest request = Fixture(false);
        DotNetCallAbiProvider provider = new();
        NativeCallPlan first = Direct(provider.Assess(request), request);
        InteropFunction function = request.module.functions[0];
        CallAbiRequest expanded = ReplaceModule(request, function: function with
        { parameters = function.parameters.Add(new(new("second"), "second", function.returnType)) });
        NativeCallPlan next = Direct(provider.Assess(expanded), expanded);
        Assert.NotSame(first, next);
        Assert.Equal(2, next.parameters.Length);
        InteropType special = request.module.types[0] with
        { nativeName = "long double", primitiveKind = "LongDouble", isSigned = null, size = 16, alignment = 16 };
        foreach (CallAbiRequest changed in new[]
        {
            ReplaceModule(request, width: 4),
            ReplaceModule(request, order: NativeByteOrder.BigEndian),
            ReplaceModule(request, type: special),
            ReplaceModule(request, function: function with { isVariadic = true }),
            ReplaceModule(request, function: function with { callingConvention = "unimplemented" })
        })
        {
            CallAbiAssessment rejected = provider.Assess(changed);
            Assert.NotEqual(NativeCallDisposition.Direct, rejected.disposition);
            Assert.Null(rejected.plan);
            Direct(provider.Assess(request), request);
        }
    }

    [Fact]
    public void CopiedRuntimeAndTargetInputsRequireTheirOwnIndependentProfileFacts()
    {
        CallAbiRequest request = Fixture(false);
        DotNetCallAbiProvider provider = new();
        NativeCallPlan original = Direct(provider.Assess(request), request);
        ManagedRuntimeProfile external = request.runtime with
        { moduleMechanism = NativeModuleMechanism.ExternalFunctionTable, importModes = [BindingImportMode.FunctionTable] };
        CallAbiRequest copied = request with { runtime = external };
        NativeCallPlan changed = Direct(provider.Assess(copied), copied);
        Assert.NotEqual(original.runtimeProfileDigest, changed.runtimeProfileDigest);
        Assert.True(new DotNetRuntimeProvider().TryResolve(new(request.runtime.runtimeId, request.target), out ManagedRuntimeProfile? expected));
        NativeCallPlan independent = Direct(DotNetCallAbi.Assess(copied, expected!), copied);
        Assert.Equal(changed.runtimeProfileDigest, independent.runtimeProfileDigest);
        NativeTargetDescriptor unsupported = new(request.target.targetId, request.target.platformId, request.target.architectureId,
            request.target.abiId, request.target.triple, request.target.toolchain, new(false, false), request.target.byteOrder);
        foreach (CallAbiRequest rejected in new[]
        {
            request with { target = unsupported },
            request with { runtime = request.runtime with { importModes = default } },
            request with { runtime = request.runtime with { nativeTriple = "other-triple" } },
            request with { runtime = request.runtime with { runtimeId = new("other.runtime") } }
        })
        {
            CallAbiAssessment result = provider.Assess(rejected);
            Assert.NotEqual(NativeCallDisposition.Direct, result.disposition);
            Assert.Null(result.plan);
            Direct(provider.Assess(request), request);
        }
        DotNetCallAbiProvider fresh = new();
        CallAbiAssessment defaultImports = fresh.Assess(request with { runtime = request.runtime with { importModes = default } });
        Assert.NotEqual(NativeCallDisposition.Direct, defaultImports.disposition);
        Assert.Null(defaultImports.plan);
        Direct(fresh.Assess(request), request);
    }

    [Fact]
    public async Task ConcurrentAlternatingBindingsCannotPublishMixedTargetAndRuntimeFacts()
    {
        CallAbiRequest first = Fixture(false), second = Fixture(true);
        DotNetCallAbiProvider provider = new();
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            CallAbiRequest request = index % 2 == 0 ? first : second;
            for (int iteration = 0; iteration < 4; iteration++) Direct(provider.Assess(request), request);
        })));
        Direct(provider.Assess(first), first);
        Direct(provider.Assess(second), second);
    }

    private static NativeCallPlan Direct(CallAbiAssessment assessment, CallAbiRequest request)
    {
        Assert.Equal(NativeCallDisposition.Direct, assessment.disposition);
        NativeCallPlan plan = Assert.IsType<NativeCallPlan>(assessment.plan);
        new CallPlanValidator(request.module, request.runtime).Verify(plan);
        Assert.Equal(ManagedRuntimeProfileIdentity.Capture(request.runtime), plan.runtimeProfileDigest);
        Assert.Equal(request.target.triple, plan.nativeTriple);
        return plan;
    }

    private static CallAbiRequest ReplaceModule(CallAbiRequest request, int? width = null, NativeByteOrder? order = null,
        InteropType? type = null, InteropFunction? function = null)
    {
        InteropFunction selected = function ?? request.module.functions[0];
        InteropModule module = new(request.module.libraryId, request.module.targetId, width ?? request.module.pointerSize,
            [type ?? request.module.types[0]], [selected], []) { byteOrder = order ?? request.module.byteOrder };
        return request with { module = module, signature = NativeCallSignature.FromFunction(selected) };
    }

    private static CallAbiRequest Fixture(bool linux)
    {
        NativeTargetDescriptor target = linux
            ? new(new("linux-x64-gnu"), "linux", "x64", "gnu", "x86_64-unknown-linux-gnu", new(), new(false, true), NativeByteOrder.LittleEndian)
            : new(new("windows-x64-msvc"), "windows", "x64", "msvc", "x86_64-pc-windows-msvc", new(), new(false, true), NativeByteOrder.LittleEndian);
        Assert.True(new DotNetRuntimeProvider().TryResolve(new(new("dotnet.coreclr"), target), out ManagedRuntimeProfile? runtime));
        InteropType integer = new(new("integer"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropFunction function = new(new("echo"), "echo", integer.id, [new(new("value"), "value", integer.id)], "C");
        InteropModule module = new("fixture", target.targetId.value, 8, [integer], [function], []) { byteOrder = NativeByteOrder.LittleEndian };
        return new(module, target, runtime!, NativeCallSignature.FromFunction(function));
    }
}
