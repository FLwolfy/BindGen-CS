using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Planning;

internal static class CallbackContextPlanner
{
    internal static NativeBridgePlan Apply(NativeBridgePlan plan)
    {
        List<BridgeType> types = plan.types.ToList();
        List<BridgeExport> exports = plan.exports.ToList();
        List<NativeCallbackContextPlan> contexts = [];
        foreach (var group in plan.aggregateCallbacks.Where(adapter => adapter.contract.retained == true)
            .GroupBy(adapter => adapter.sourceFunction))
        {
            AggregateCallbackAdapter first = group.First();
            CallbackContract contract = first.contract;
            if (!Strong(contract.evidence) || !Strong(contract.lifetime.evidence) || !Strong(contract.registration?.evidence)
                || contract.unregisterFunction is not StableId unregister || contract.registration is not
                {
                    kind: NativeCallbackRegistrationKind.ReturnedHandle, unregisterHandleParameter: StableId handleParameter,
                    nullResultQuiescence: NativeQuiescenceKind.NoActiveNativeInvocations
                }
                || contract.unregisterQuiescence != NativeQuiescenceKind.NoActiveNativeInvocations)
                throw new NotSupportedException("Retained aggregate callbacks require an explicit returned-handle stop-and-join protocol with complete native-frame quiescence.");
            AggregateCallbackAdapter[] all = plan.aggregateCallbacks.Where(adapter => adapter.sourceFunction == group.Key).ToArray();
            if (all.Any(adapter => adapter.userDataParameter != first.userDataParameter
                || adapter.contract.retained != true || adapter.contract.unregisterFunction != unregister
                || !Strong(adapter.contract.evidence) || !Strong(adapter.contract.registration?.evidence)
                || !SameRegistration(adapter.contract.registration, contract.registration)))
                throw new NotSupportedException("Callbacks retained by one native registration require one explicit shared context and shutdown protocol.");
            BridgeExport registration = exports.Single(export => export.operation.declaration == group.Key);
            NativeDeclaration native = plan.source.GetDeclaration(group.Key);
            if (native.returnType is not StableId handle || Resolve(handle).category != NativeTypeCategory.Pointer)
                throw new NotSupportedException("A retained callback bridge needs a pointer registration handle; other identity protocols require a callable lowering.");
            BridgeExport stop = exports.SingleOrDefault(export => export.operation.declaration == unregister)
                ?? throw new NotSupportedException("The explicit callback stop-and-join declaration must be included in the selected binding surface.");
            NativeDeclaration stopHandle = plan.source.GetDeclaration(handleParameter);
            if (plan.source.GetDeclaration(unregister).children.All(id => id != handleParameter)
                || stopHandle.type is not StableId stopType || Resolve(stopType).category != NativeTypeCategory.Pointer)
                throw new InvalidOperationException("The callback stop handle does not belong to its declared native operation.");
            StableId carrier = new("native-bridge-registration:" + group.Key.value);
            types.Add(new(carrier, BridgeTypeKind.Handle, registration.name + "_registration") { nativeType = handle });
            exports[exports.IndexOf(registration)] = registration with { resultType = carrier, resultInOutputParameter = false };
            ReplaceHandle(stop, handleParameter, carrier);
            InteropFunction semantics = plan.sourceSemantics!.functions.Single(function => function.id == group.Key);
            NativeCallbackContextPlan context = new(group.Key, unregister, handleParameter, handle, carrier,
                all.Select(adapter => adapter.sourceParameter).ToImmutableArray());
            if (semantics.asynchronous is { cancelFunction: StableId cancel, cancellationHandleParameter: StableId cancelHandle })
            {
                BridgeExport cancellation = exports.SingleOrDefault(export => export.operation.declaration == cancel)
                    ?? throw new NotSupportedException("The asynchronous cancellation declaration must be selected with its registration.");
                if (cancel == unregister) throw new NotSupportedException("Cancellation and stop-and-join cannot share an ambiguous borrowed/releasing C wrapper.");
                ReplaceHandle(cancellation, cancelHandle, carrier);
                context = context with { cancelFunction = cancel, cancellationHandleParameter = cancelHandle };
            }
            if (contexts.Any(existing => existing.unregisterFunction == unregister || existing.cancelFunction == unregister
                || context.cancelFunction is StableId cancellation && (existing.unregisterFunction == cancellation || existing.cancelFunction == cancellation)))
                throw new NotSupportedException("Shared native stop operations require an explicit joint callable lowering for their registration domains.");
            contexts.Add(context);
        }
        return plan with
        {
            types = types.OrderBy(type => type.id.value, StringComparer.Ordinal).ToImmutableArray(),
            exports = exports.ToImmutableArray(),
            callbackContexts = contexts.ToImmutableArray()
        };

        static bool SameRegistration(
            CallbackRegistrationContract? left,
            CallbackRegistrationContract? right
        )
            => left is not null && right is not null && left.kind == right.kind && left.unregisterHandleParameter == right.unregisterHandleParameter
                && left.nullResultQuiescence == right.nullResultQuiescence && left.releaseThreadDomain == right.releaseThreadDomain
                && left.unregisterCallbackParameter == right.unregisterCallbackParameter && left.unregisterUserDataParameter == right.unregisterUserDataParameter
                && left.successfulReturn == right.successfulReturn && left.failureResultQuiescence == right.failureResultQuiescence
                && left.unregisterIsNonBlocking == right.unregisterIsNonBlocking;
        static bool Strong(Evidence? evidence) => evidence is { level: EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified }
            && !string.IsNullOrWhiteSpace(evidence.origin);
        NativeType Resolve(StableId id) => StandardLibraryFactReader.Resolve(plan.source, id);
        void ReplaceHandle(
            BridgeExport export,
            StableId parameter,
            StableId carrier
        ) {
            NativeDeclaration[] original = plan.source.GetDeclaration(export.operation.declaration!.Value).children
                .Select(plan.source.GetDeclaration).Where(item => item.kind == "Parameter").ToArray();
            int index = Array.FindIndex(original, item => item.id == parameter);
            if (index < 0 || original.Length != export.parameters.Length || original[index].type is not StableId nativeHandle
                || Resolve(nativeHandle).category != NativeTypeCategory.Pointer)
                throw new InvalidOperationException("A callback context handle cannot be associated with its original native parameter.");
            exports[exports.IndexOf(export)] = export with
            { parameters = export.parameters.SetItem(index, export.parameters[index] with { type = carrier }) };
        }
    }
}
