using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

internal static class ValueStringReceiverPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        var omissions = plan.ownedProjectionOmissions.ToBuilder();
        var results = ImmutableArray.CreateBuilder<CSharpValueStringResultPlan>();
        foreach (CSharpValueStringResultPlan item in plan.valueStringResults)
        {
            CSharpValueStringResultPlan attached = Attach(item);
            if (attached.requiresOwnedReceiver && attached.receiver is null)
            {
                InteropFunction function = plan.api.interop.functions.Single(function => function.id == plan.methods[item.factory.methodIndex].nativeExport);
                omissions.Add(new(function.id, function.parameters[item.factory.resultIndex].id, function.source,
                    "A native text member with unknown self retention requires an exact existing owned wrapper; no raw borrowed-pointer convenience is inferred."));
            }
            else results.Add(attached);
        }
        return plan with { valueStringResults = results.ToImmutable(), ownedProjectionOmissions = omissions.ToImmutable() };

        CSharpValueStringResultPlan Attach(CSharpValueStringResultPlan operation)
        {
            InteropFunction function = plan.api.interop.functions.Single(function => function.id == plan.methods[operation.factory.methodIndex].nativeExport);
            if (function.sourceOwnerType is not StableId owner) return operation;
            if (plan.api.methods.Single(member => member.nativeExport == function.id).receiver is { kind: not ManagedReceiverKind.Owned }) return operation;
            CSharpObjectPlan? wrapper = plan.objects.SingleOrDefault(item => item.nativeType == owner && item.kind == ManagedReceiverKind.Owned);
            if (wrapper is null) return operation;
            int[] receivers = operation.factory.inputIndices.Where(index =>
            {
                InteropParameter input = function.parameters[index];
                InteropType pointer = Resolve(input.type);
                return pointer.category == NativeTypeCategory.Pointer && pointer.elementType is StableId element && Resolve(element).sourceNativeType == owner
                    && input.text is null && input.buffer is null && input.singleValue is null && input.callback is null
                    && input.ownership is { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call, retainsPointer: not true } borrowed
                    && Known(borrowed.evidence) && Known(borrowed.lifetime.evidence);
            }).ToArray();
            if (receivers.Length != 1) return operation;
            int receiver = receivers[0];
            if (plan.methods[operation.factory.methodIndex].valueProjections.ContainsKey(receiver)) return operation;
            CSharpStatusMethodPlan[] factories = plan.statusMethods.Where(factory => factory.ownedResult?.objectName == wrapper.name).ToArray();
            if (factories.Length == 0) return operation;
            foreach (CSharpStatusMethodPlan factory in factories)
            {
                InteropFunction creator = plan.api.interop.functions.Single(function => function.id == plan.methods[factory.methodIndex].nativeExport);
                InteropType output = Resolve(creator.parameters[factory.resultIndex].type);
                if (output.elementType is not StableId pointer || !NativeReceiverTypeMatcher.CanBorrowWithoutRemovingQualifiers(plan.api.interop, pointer, function.parameters[receiver].type))
                    return operation;
            }
            CSharpFacadeMethodPlan original = operation.signature;
            int[] visible = Enumerable.Range(0, original.inputIndices.Length).Where(index => original.inputIndices[index] != receiver).ToArray();
            CSharpFacadeMethodPlan signature = original with
            {
                parameterTypes = visible.Select(index => original.parameterTypes[index]).ToImmutableArray(),
                parameterNames = visible.Select(index => original.parameterNames[index]).ToImmutableArray(),
                parameterModifiers = visible.Select(index => FacadeParameters.Modifier(original, index)).ToImmutableArray(),
                inputIndices = visible.Select(index => original.inputIndices[index]).ToImmutableArray(),
                documentationContent = original.documentationContent is { } documentation ? documentation with { parameters = visible.Select(index => documentation.parameters[index]).ToImmutableArray() } : null
            };
            int contextReceiver = original.inputIndices.IndexOf(receiver);
            return operation with
            {
                receiver = new(wrapper.name, receiver, signature),
                signature = original with { parameterTypes = original.parameterTypes.SetItem(contextReceiver, SharedContextPlanner.ContextType(plan) + "." + wrapper.name) }
            };
        }
        InteropType Resolve(StableId id)
        {
            HashSet<StableId> active = [];
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!active.Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("A text member receiver has an incomplete or cyclic type graph.");
                type = plan.api.interop.GetType(next);
            }
            return type;
        }
    }

    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified && !string.IsNullOrWhiteSpace(evidence.origin);
}
