using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Analysis.Calls;
using BGCS.Composition;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Targets;

namespace BGCS;

internal sealed class NativeCallPlanningStage
{
    private readonly NativeTargetDescriptor m_target;
    private readonly ImmutableArray<ICallAbiProvider> m_abis;
    internal ManagedRuntimeProfile runtime { get; }

    internal NativeCallPlanningStage(
        FrozenExtensionRegistry registry,
        NativeTargetDescriptor target,
        ManagedRuntimeId? requestedRuntime,
        GenerationInputTracker inputs,
        CancellationToken cancellationToken
    ) {
        m_target = target;
        List<ManagedRuntimeProfile> matches = [];
        foreach (ServiceFactory<IManagedRuntimeProvider> registration in registry.Runtimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IManagedRuntimeProvider provider = registration.Factory();
            inputs.Capture(provider, target, cancellationToken);
            if (provider.TryResolve(new(requestedRuntime ?? default, target), out ManagedRuntimeProfile? profile))
            {
                if (profile is null || requestedRuntime is { } explicitRuntime && explicitRuntime != profile.runtimeId)
                    throw new InvalidOperationException("A runtime provider changed the explicitly selected execution model.");
                matches.Add(profile);
            }
        }
        runtime = matches.Count == 1 ? matches[0] : throw new InvalidOperationException(matches.Count == 0
            ? "No registered runtime provider establishes the requested target and execution model. Select api.runtime and its implementation explicitly."
            : "Multiple runtime providers claim the same execution model. Select one unambiguous implementation before generation.");
        if (runtime.nativeTargetId != target.targetId.value || runtime.nativeTriple != target.triple)
            throw new InvalidOperationException("The execution profile does not belong to the resolved target.");
        m_abis = registry.CallAbis.Select(registration =>
        {
            ICallAbiProvider provider = registration.Factory();
            inputs.Capture(provider, target, cancellationToken);
            return provider;
        }).ToImmutableArray();
    }

    internal CallAbiAssessment Assess(
        InteropModule module,
        NativeCallSignature signature
    ) => Assess(module, signature, new CallPlanValidator(module, runtime));

    private CallAbiAssessment Assess(
        InteropModule module,
        NativeCallSignature signature,
        CallPlanValidator validation
    ) {
        CallAbiAssessment? claim = null;
        int claimCount = 0;
        foreach (ICallAbiProvider provider in m_abis)
        {
            CallAbiAssessment assessment = provider.Assess(new(module, m_target, runtime, signature));
            if (assessment.disposition == NativeCallDisposition.NotApplicable) continue;
            claim = assessment;
            claimCount++;
        }
        if (claimCount != 1) throw new NotSupportedException(claimCount == 0
            ? "No registered calling-ABI implementation owns the selected execution profile."
            : "Multiple calling-ABI implementations claim the same physical signature.");
        CallAbiAssessment result = claim!;
        if (result.provider != runtime.callAbi || !Enum.IsDefined(result.disposition)
            || string.IsNullOrWhiteSpace(result.reason))
            throw new InvalidOperationException("A calling-ABI assessment has incomplete or conflicting identity.");
        if (result.rejectionType is StableId rejected
            && (result.disposition == NativeCallDisposition.Direct || !module.types.Any(type => type.id == rejected)))
            throw new InvalidOperationException("A calling-ABI rejection named a foreign type or contradicted its direct-call plan.");
        if (result.disposition == NativeCallDisposition.Direct)
        {
            if (result.plan is null || !CallPlanValidator.SameSignature(signature, result.plan.signature))
                throw new InvalidOperationException("A direct ABI assessment omitted or changed its physical signature.");
            validation.Verify(result.plan);
        }
        else if (result.plan is not null) throw new InvalidOperationException("Only a direct ABI assessment may carry a direct-call plan.");
        return result;
    }

    internal (ImmutableArray<NativeCallPlan> plans, ImmutableArray<BindingDiagnostic> diagnostics) PlanFinal(
        InteropModule module,
        CancellationToken cancellationToken
    ) {
        CallPlanValidator validation = new(module, runtime);
        ImmutableArray<NativeCallPlan>.Builder plans = ImmutableArray.CreateBuilder<NativeCallPlan>();
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        foreach (InteropFunction function in module.functions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallAbiAssessment result = Assess(module, NativeCallSignature.FromFunction(function), validation);
            if (result.disposition == NativeCallDisposition.Direct) plans.Add(result.plan!);
            else Reject(result, function.source,
                $"Final export '{function.exportName}' has no safe direct carrier after native lowering: {result.reason}");
        }
        HashSet<StableId> requiredCallbacks = RequiredCallbackTypes(module);
        foreach (InteropType type in module.types.Where(type => type.category == NativeTypeCategory.Function && type.hasPrototype && !type.isVariadic))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallAbiAssessment result = Assess(module, NativeCallSignature.FromCallback(type), validation);
            if (result.disposition == NativeCallDisposition.Direct) plans.Add(result.plan!);
            else if (requiredCallbacks.Contains(type.id))
                Reject(result, type.source, $"Callback '{type.nativeName}' has no safe reverse-call carrier after native lowering: {result.reason}");
        }
        return (diagnostics.Count == 0 ? plans.ToImmutable() : [], diagnostics.ToImmutable());

        void Reject(
            CallAbiAssessment assessment,
            SourceLocation? source,
            string message
        ) {
            diagnostics.Add(new(BindingDiagnosticSeverity.Error, message, "BGCS-CALL-ABI")
            {
                location = assessment.rejectionType is StableId id ? module.GetType(id).source ?? source : source
            });
        }
    }

    internal static bool HasAggregateBoundary(InteropModule module)
    {
        foreach (InteropFunction function in module.functions)
        {
            if (Aggregate(function.returnType) || function.parameters.Any(parameter => Aggregate(parameter.type))) return true;
        }
        return RequiredCallbackTypes(module).Any(id =>
        {
            InteropType callback = module.GetType(id);
            return callback.returnType is StableId result && Aggregate(result) || callback.parameterTypes.Any(Aggregate);
        });

        bool Aggregate(StableId id)
        {
            InteropType type = Resolve(module, id);
            return type.category is NativeTypeCategory.Record or NativeTypeCategory.Array;
        }
    }

    private static HashSet<StableId> RequiredCallbackTypes(InteropModule module)
    {
        HashSet<StableId> callbacks = [];
        HashSet<StableId> visited = [];
        foreach (InteropFunction function in module.functions)
        {
            foreach (InteropParameter parameter in function.parameters) Visit(parameter.type);
            Visit(function.returnType);
        }
        return callbacks;

        void Visit(StableId id)
        {
            if (!visited.Add(id)) return;
            InteropType type = module.GetType(id);
            if (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Pointer)
            {
                if (type.elementType is StableId element) Visit(element);
                return;
            }
            // Incomplete and variadic function pointers are transported only as opaque
            // native addresses. No managed invocation or reverse thunk is generated.
            if (type.category != NativeTypeCategory.Function || !type.hasPrototype || type.isVariadic) return;
            callbacks.Add(type.id);
            foreach (StableId parameter in type.parameterTypes) Visit(parameter);
            if (type.returnType is StableId result) Visit(result);
        }
    }

    private static InteropType Resolve(
        InteropModule module,
        StableId id
    ) {
        HashSet<StableId>? visited = null;
        InteropType type = module.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId next)
        {
            if (!(visited ??= []).Add(type.id)) throw new InvalidOperationException("A native alias cycle cannot be classified.");
            type = module.GetType(next);
        }
        return type;
    }
}
