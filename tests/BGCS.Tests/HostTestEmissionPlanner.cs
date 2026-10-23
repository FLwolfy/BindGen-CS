using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Analysis.Calls;
using BGCS.Core.Calls;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using BGCS.Interop.DotNet;
using BGCS.Targets.Desktop.Targeting;

namespace BGCS.Tests;

// Detached planning assertions select the same public .NET classifier explicitly.
// They do not manufacture Verified layout evidence or grant another platform an ABI.
internal sealed class HostTestEmissionPlanner
{
    internal CSharpEmissionPlan Plan(
        ManagedApiModel model,
        EmissionRequest request,
        CancellationToken cancellationToken = default
    ) {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new(model.interop.targetId)));
        ManagedRuntimeProfile established = TestComposition.ResolveRuntime(target);
        ManagedRuntimeProfile selected = request.managedRuntime ?? established;
        selected = selected with
        {
            nativeTargetId = string.IsNullOrEmpty(selected.nativeTargetId) ? target.targetId.value : selected.nativeTargetId,
            nativeTriple = string.IsNullOrEmpty(selected.nativeTriple) ? target.triple : selected.nativeTriple,
            pointerSize = selected.pointerSize == 0 ? established.pointerSize : selected.pointerSize,
            byteOrder = selected.byteOrder == BGCS.Core.Native.NativeByteOrder.Unknown ? established.byteOrder : selected.byteOrder,
            callAbi = string.IsNullOrEmpty(selected.callAbi.value) ? established.callAbi : selected.callAbi
        };
        var plans = ImmutableArray.CreateBuilder<NativeCallPlan>();
        foreach (NativeCallSignature signature in model.interop.functions.Select(NativeCallSignature.FromFunction)
            .Concat(model.interop.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Function && type.returnType is not null && type.hasPrototype)
                .Select(NativeCallSignature.FromCallback)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallAbiAssessment assessed = DotNetCallAbi.Assess(new(model.interop, target, selected, signature), selected);
            if (assessed.disposition == NativeCallDisposition.Direct) plans.Add(assessed.plan!);
        }
        return new CSharpEmissionPlanner().Plan(model, request with { managedRuntime = selected, callPlans = plans.ToImmutable() }, cancellationToken);
    }
}
