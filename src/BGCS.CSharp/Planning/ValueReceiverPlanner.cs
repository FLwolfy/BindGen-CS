using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.CSharp.Emission;

namespace BGCS.CSharp.Planning;

internal static class ValueReceiverPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        ManagedMember[] selected = plan.api.methods.Where(member => member.receiver?.kind == ManagedReceiverKind.Value).ToArray();
        if (selected.Length == 0) return plan;
        var storage = plan.storage.ToBuilder();
        var omissions = plan.valueParameterOmissions.ToBuilder();
        Dictionary<StableId, InteropFunction> exports = plan.api.interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, int> methodIndices = plan.methods.Select((
            method,
            index
        ) => (method.nativeExport, index)).ToDictionary(item => item.nativeExport, item => item.index);
        Dictionary<StableId, int> carrierIndices = plan.storage.Select((
            carrier,
            index
        ) => (carrier.nativeType, index)).ToDictionary(item => item.nativeType, item => item.index);
        Dictionary<StableId, InteropType> types = plan.api.interop.types.ToDictionary(type => type.id);
        Dictionary<StableId, ManagedType> managedTypes = plan.api.types.ToDictionary(type => type.nativeType);
        foreach (ManagedMember member in selected)
        {
            ManagedReceiver receiver = member.receiver!;
            InteropFunction function = exports[member.nativeExport];
            if (!methodIndices.TryGetValue(member.nativeExport, out int methodIndex)) throw new InvalidOperationException("A value receiver lost its exact raw import.");
            CSharpMethodPlan method = plan.methods[methodIndex];
            int self = Array.FindIndex(function.parameters.ToArray(), parameter => parameter.id == receiver.parameter);
            InteropType native = types[receiver.nativeType];
            int carrierIndex = carrierIndices.TryGetValue(native.id, out int foundCarrier) ? foundCarrier : -1;
            if (self < 0 || native.category != NativeTypeCategory.Record
                || !NativeReceiverTypeMatcher.Matches(plan.api.interop, native.id, function.parameters[self].type))
                throw new ArgumentException("A value receiver must preserve one exact native record pointer and parameter identity.");
            if (receiver.requiredReleaseDomain is not null || receiver.objectName != CSharpNames.Identifier(managedTypes[native.id].name))
                throw new ArgumentException("A value receiver uses its existing carrier without an object name or release domain.");
            string? restriction = Restriction();
            if (restriction is not null)
            {
                omissions.Add(new(function.id, receiver.parameter, function.parameters[self].source ?? function.source, restriction));
                continue;
            }
            CSharpStoragePlan plannedCarrier = storage[carrierIndex];
            CSharpValueParameterPlan plannedValue = method.valueParameters.Single(value => value.parameterIndex == self);
            int[] inputs = OverloadPlanner.PublicInputs(method, Enumerable.Range(0, function.parameters.Length)).ToArray();
            int[] publicInputs = inputs.Where(index => index != self).ToArray();
            string context = "__bgcsContext";
            while (publicInputs.Any(index => method.parameterNames[index].TrimStart('@') == context)) context += "_";
            CSharpCallableDocumentationPlan? documentation = method.documentationContent is { } content
                ? content with
                {
                    parameters = ImmutableArray.Create<string?>("The binding context that performs this native call.")
                    .AddRange(publicInputs.Select(index => content.parameters[index])),
                    notes = string.IsNullOrWhiteSpace(content.parameters[self]) ? content.notes : content.notes.Add("Native receiver: " + content.parameters[self])
                } : null;
            CSharpFacadeMethodPlan signature = new(method.name, RecipeConversionEmitter.DirectResultType(method),
                ImmutableArray.Create(SharedContextPlanner.ContextType(plan)).AddRange(publicInputs.Select(index => OverloadPlanner.InputType(method, index))),
                ImmutableArray.Create(context).AddRange(publicInputs.Select(index => method.parameterNames[index])))
            {
                nativeExport = function.id,
                inputIndices = ImmutableArray.Create(-1).AddRange(publicInputs.AsEnumerable()),
                parameterModifiers = ImmutableArray.Create(string.Empty).AddRange(publicInputs.Select(index => OverloadPlanner.InputModifier(method, index))),
                documentation = method.documentation,
                documentationContent = documentation
            };
            CSharpFacadeMethodPlan target = new(method.name, signature.returnType,
                inputs.Select(index => OverloadPlanner.InputType(method, index)).ToImmutableArray(),
                inputs.Select(index => method.parameterNames[index]).ToImmutableArray())
            {
                nativeExport = function.id,
                inputIndices = inputs.ToImmutableArray(),
                parameterModifiers = inputs.Select(index => OverloadPlanner.InputModifier(method, index)).ToImmutableArray()
            };
            storage[carrierIndex] = plannedCarrier with
            {
                valueMembers = plannedCarrier.valueMembers.Add(new(methodIndex, self,
                plannedValue.direction == NativeParameterDirection.Input, signature, target))
            };

            string? Restriction()
            {
                if (carrierIndex < 0 || native.size is not > 0) return "A value receiver requires complete independently generated native record storage.";
                ValueParameterAnalyzer analyzer = new();
                if (analyzer.GetRestriction(plan.api.interop, function, function.parameters[self]) is string failure)
                    return "The value receiver has no safe original-storage borrow: " + failure;
                if (function.parameters[self].ownership?.retainsPointer == true)
                    return "A value receiver cannot contradict an explicit native pointer-retention contract.";
                CSharpValueParameterPlan? value = method.valueParameters.SingleOrDefault(candidate => candidate.parameterIndex == self);
                if (value is null || value.direction is not (NativeParameterDirection.Input or NativeParameterDirection.InputOutput))
                    return "A value receiver requires an existing checked in or ref borrow; output-only receivers are not instance values.";
                CSharpStoragePlan carrier = storage[carrierIndex];
                if (carrier.size != native.size || carrier.managedAlignment != native.alignment || carrier.managedAlignment is null
                    || !carrier.bitfields.IsEmpty || !carrier.flexibleArrays.IsEmpty || carrier.arrayLength is not null)
                    return "A value receiver requires exact addressable record storage with verified managed alignment.";
                if (method.valueProjections.ContainsKey(self) || OverloadPlanner.InputType(method, self) != value.elementType)
                    return "A value receiver cannot replace its original raw storage with a copied managed projection.";
                if (!QualifiersKnown(function.parameters[self].type)) return "A value receiver cannot erase volatile or unknown native pointer qualifications.";
                InteropType result = types[function.returnType];
                while (result.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) result = types[result.elementType!.Value];
                if (result.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration))
                    return "A value receiver currently requires a scalar or void native result.";
                if (!OverloadPlanner.IsDirect(plan, method, function))
                    return "A value receiver currently requires an existing synchronous direct scalar/void operation with checked input protocols.";
                return null;
            }

            bool QualifiersKnown(StableId id)
            {
                HashSet<StableId> visited = [];
                while (true)
                {
                    if (!visited.Add(id)) throw new InvalidOperationException("A value receiver qualifier graph is cyclic.");
                    InteropType view = types[id];
                    if (view.qualifier is not (null or "Const")) return false;
                    if (view.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Pointer)) return view.id == native.id;
                    if (view.elementType is not StableId element) return false;
                    id = element;
                }
            }
        }
        return plan with { storage = storage.ToImmutable(), valueParameterOmissions = omissions.ToImmutable() };
    }
}

/// <summary>Plans a value-carrier member forwarding an original in/ref receiver through an explicit context.</summary>
/// <param name="methodIndex">The unchanged raw import index.</param>
/// <param name="receiverIndex">The exact native receiver parameter index.</param>
/// <param name="isReadOnly">Whether the original record is borrowed through an in parameter.</param>
/// <param name="signature">The public carrier member, beginning with its explicit binding context.</param>
/// <param name="target">The existing checked context operation.</param>
public sealed record CSharpValueReceiverPlan(
    int methodIndex,
    int receiverIndex,
    bool isReadOnly,
    CSharpFacadeMethodPlan signature,
    CSharpFacadeMethodPlan target
);
