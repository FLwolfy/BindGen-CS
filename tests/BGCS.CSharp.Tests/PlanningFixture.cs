using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Tests;

// Syntax and mechanism fixtures explicitly select their independent scalar provider.
// It grants no aggregate ABI, operating-system support or runtime certification.
internal static class PlanningFixture
{
    internal static EmissionRequest Prepare(
        ManagedApiModel model,
        EmissionRequest request
    ) {
        InteropModule module = model.interop;
        ManagedRuntimeProfile profile = request.managedRuntime ?? new("fixture.scalar/v1", new("fixture.syntax"),
            NativeModuleMechanism.DynamicLibrary, [BindingImportMode.FunctionTable, BindingImportMode.DllImport, BindingImportMode.LibraryImport], false, true);
        profile = profile with
        {
            nativeTargetId = profile.nativeTargetId.Length == 0 ? module.targetId : profile.nativeTargetId,
            nativeTriple = profile.nativeTriple.Length == 0 ? "fixture-explicit-triple" : profile.nativeTriple,
            pointerSize = profile.pointerSize == 0 ? module.pointerSize : profile.pointerSize,
            byteOrder = profile.byteOrder == NativeByteOrder.Unknown ? module.byteOrder : profile.byteOrder,
            callAbi = string.IsNullOrEmpty(profile.callAbi.value) ? new("fixture.scalar-call") : profile.callAbi
        };
        if (!request.callPlans.IsEmpty) return request with { managedRuntime = profile };
        var plans = ImmutableArray.CreateBuilder<NativeCallPlan>();
        ScalarProvider provider = new();
        NativeTargetDescriptor target = new(new(module.targetId), "fixture", "fixture", "fixture", profile.nativeTriple,
            new(), new(false, profile.moduleMechanism == NativeModuleMechanism.DynamicLibrary), module.byteOrder);
        foreach (NativeCallSignature signature in module.functions.Select(NativeCallSignature.FromFunction)
            .Concat(module.types.Where(type => type.category == NativeTypeCategory.Function && type.returnType is not null && type.hasPrototype)
                .Select(NativeCallSignature.FromCallback)))
        {
            CallAbiAssessment assessment = provider.Assess(new(module, target, profile, signature));
            if (assessment.disposition == NativeCallDisposition.Direct) plans.Add(assessment.plan!);
        }
        return request with { managedRuntime = profile, callPlans = plans.ToImmutable() };
    }

    private sealed class ScalarProvider : ICallAbiProvider
    {
        public CallAbiAssessment Assess(CallAbiRequest request)
        {
            if (request.signature.isVariadic || !request.signature.hasPrototype)
                return new(request.runtime.callAbi, NativeCallDisposition.Unsupported, "The fixture has no variadic or incomplete-call implementation.");
            try
            {
                NativeCallValuePlan? result = Value(request.signature.returnType, true);
                var parameters = request.signature.parameterTypes.Select(id => Value(id, false)!).ToImmutableArray();
                NativeCallPlan plan = new(request.signature, request.module.targetId, request.runtime.runtimeId, request.runtime.id,
                    request.runtime.callAbi, request.module.pointerSize, request.module.byteOrder, result, parameters)
                { nativeTriple = request.target.triple, runtimeProfileDigest = ManagedRuntimeProfileIdentity.Capture(request.runtime) };
                return new(request.runtime.callAbi, NativeCallDisposition.Direct, "Independent scalar syntax fixture; no aggregate or platform certification.", plan);
            }
            catch (NotSupportedException failure) { return new(request.runtime.callAbi, NativeCallDisposition.RequiresBridge, failure.Message); }

            NativeCallValuePlan? Value(
                StableId id,
                bool allowVoid
            ) {
                HashSet<StableId> seen = [];
                InteropType type = request.module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Enumeration)
                {
                    if (!seen.Add(type.id) || type.elementType is not StableId next) throw new NotSupportedException("Incomplete fixture alias.");
                    type = request.module.GetType(next);
                }
                if (type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Void" && allowVoid) return null;
                if (type.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Pointer)
                    || type.size is not > 0 || type.alignment is not > 0)
                    throw new NotSupportedException("The syntax fixture provides only complete scalar and pointer call channels.");
                return new(id, type.size.Value, type.alignment.Value, ["FixtureScalar"]);
            }
        }
    }
}

internal sealed class CSharpTestPlanner
{
    internal CSharpEmissionPlan Plan(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken = default
    )
        => new CSharpEmissionPlanner().Plan(model, PlanningFixture.Prepare(model, request), cancellationToken);
}

internal sealed class CSharpTestBackend
{
    internal ValueTask<ImmutableArray<ArtifactDescriptor>> EmitAsync(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken
    )
        => new CSharpBackend().EmitAsync(model, PlanningFixture.Prepare(model, request), cancellationToken);
}
