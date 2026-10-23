using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.CSharp.Emission;

namespace BGCS.CSharp.Planning;

internal static class DirectReceiverObjectPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        var objects = plan.objects.ToBuilder();
        var factories = plan.directOwnedFactories.ToBuilder();
        var borrowedFactories = plan.directBorrowedFactories.ToBuilder();
        var omissions = plan.ownedProjectionOmissions.ToBuilder();
        Dictionary<StableId, InteropFunction> exports = plan.api.interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, int> importIndices = plan.methods.Select((
            method,
            index
        ) => (method.nativeExport, index))
            .ToDictionary(item => item.nativeExport, item => item.index);
        ManagedMember[] selected = plan.api.methods.Where(member => member.receiver is not null && member.receiver.kind != ManagedReceiverKind.Value).ToArray();
        HashSet<string> reserved = plan.methods.Select(method => method.name.TrimStart('@')).Concat(plan.storage.Select(storage => storage.name.TrimStart('@')))
            .Concat(plan.objects.Select(item => item.name.TrimStart('@'))).Concat(new[] { plan.contextName.TrimStart('@'), "RawApi", "Raw", "NativeImports", "RecipeConversions", "Constants", "StaticNativeAdapter" }).ToHashSet(StringComparer.Ordinal);
        foreach (var group in selected.GroupBy(member => member.receiver!.nativeType))
        {
            ManagedReceiverKind[] kinds = group.Select(member => member.receiver!.kind).Distinct().ToArray();
            if (kinds.Length != 1 || !Enum.IsDefined(kinds[0])) throw new ArgumentException("A native receiver identity has conflicting or unknown wrapper lifetime protocols.");
            bool moduleBorrowed = kinds[0] == ManagedReceiverKind.ModuleBorrowed;
            string[] names = group.Select(member => CSharpNames.Identifier(member.receiver!.objectName)).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length != 1 || !reserved.Add(names[0].TrimStart('@'))) throw new ArgumentException("A C object wrapper name conflicts with another object, raw carrier or API member.");
            int FactoryMethod(int index) => moduleBorrowed ? borrowedFactories[index].methodIndex : factories[index].methodIndex;
            int[] matching = Enumerable.Range(0, moduleBorrowed ? borrowedFactories.Count : factories.Count)
                .Where(index => NativeReceiverTypeMatcher.Matches(plan.api.interop, group.Key, Native(FactoryMethod(index)).returnType)).ToArray();
            if (matching.Length == 0)
            {
                foreach (ManagedMember member in group) Omit(member, "A typed C member requires a selected supported factory with the same exact native identity and wrapper lifetime protocol.");
                continue;
            }
            foreach (int index in matching)
            {
                if (moduleBorrowed)
                {
                    CSharpModuleBorrowedFactoryPlan factory = borrowedFactories[index];
                    if (factory.objectName is not null) throw new ArgumentException("A borrowed getter matches multiple explicitly selected C receiver identities.");
                    borrowedFactories[index] = factory with { objectName = names[0] };
                }
                else
                {
                    CSharpDirectOwnedFactoryPlan factory = factories[index];
                    if (factory.ownership.objectName is not null) throw new ArgumentException("A native factory matches multiple explicitly selected C receiver identities.");
                    factories[index] = factory with { ownership = factory.ownership with { objectName = names[0] } };
                }
            }
            ImmutableArray<CSharpReceiverMethodPlan>.Builder members = ImmutableArray.CreateBuilder<CSharpReceiverMethodPlan>();
            foreach (ManagedMember member in group)
            {
                if (!importIndices.TryGetValue(member.nativeExport, out int methodIndex)) throw new ArgumentException("A C receiver mapping lost its selected native import.");
                CSharpMethodPlan method = plan.methods[methodIndex];
                InteropFunction function = Native(methodIndex);
                int receiver = -1;
                for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == member.receiver!.parameter) receiver = index;
                if (receiver < 0 || !NativeReceiverTypeMatcher.Matches(plan.api.interop, group.Key, function.parameters[receiver].type))
                    throw new ArgumentException("A C receiver mapping changed its exact native pointer identity.");
                InteropParameter parameter = function.parameters[receiver];
                if (matching.Any(index => !NativeReceiverTypeMatcher.CanBorrowWithoutRemovingQualifiers(plan.api.interop,
                    Native(FactoryMethod(index)).returnType, parameter.type)))
                {
                    Omit(member, "A C receiver cannot remove const/volatile or unknown native pointee qualifiers from any of its selected factories.");
                    continue;
                }
                if (moduleBorrowed && member.receiver!.requiredReleaseDomain is not null)
                    throw new ArgumentException("A module-borrowed C receiver has no native allocation/release domain.");
                if (function.error is not null || function.asynchronous is not null || function.returnOwnership is not null || function.returnString is not null
                    || !OverloadPlanner.SupportsDirectResult(plan, function.returnType) || parameter.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Call, retainsPointer: false } borrowed
                    || !Known(borrowed.evidence) || !Known(borrowed.lifetime.evidence) || parameter.buffer is not null || parameter.text is not null || parameter.callback is not null
                    || parameter.singleValue is not null || method.valueProjections.ContainsKey(receiver)
                    || !Enumerable.Range(0, function.parameters.Length).Where(index => index != receiver).All(index => OverloadPlanner.SupportsDirectInput(plan, method, function, index)))
                {
                    Omit(member, "A C member requires explicit nonretaining borrowed Call ownership, a supported scalar/void result and supported synchronous input protocols.");
                    continue;
                }
                int[] contextInputs = OverloadPlanner.PublicInputs(method, Enumerable.Range(0, method.parameterTypes.Length)).ToArray();
                int[] objectInputs = contextInputs.Where(index => index != receiver).ToArray();
                string result = RecipeConversionEmitter.DirectResultType(method);
                CSharpFacadeMethodPlan Signature(
                    int[] inputs,
                    bool context
                ) => new(method.name, result,
                    inputs.Select(index => context && index == receiver ? SharedContextPlanner.ContextType(plan) + "." + names[0] : OverloadPlanner.InputType(method, index)).ToImmutableArray(),
                    inputs.Select(index => method.parameterNames[index]).ToImmutableArray())
                {
                    nativeExport = method.nativeExport,
                    inputIndices = inputs.ToImmutableArray(),
                    parameterModifiers = inputs.Select(index => index == receiver ? string.Empty : OverloadPlanner.InputModifier(method, index)).ToImmutableArray(),
                    documentation = method.documentation,
                    documentationContent = method.documentationContent is { } content
                        ? content with { parameters = inputs.Select(index => content.parameters[index]).ToImmutableArray() } : null
                };
                members.Add(new(methodIndex, receiver, Signature(contextInputs, true), Signature(objectInputs, false), member.receiver!.requiredReleaseDomain?.value));
            }
            objects.Add(new(names[0], group.Key, []) { kind = kinds[0], directMethods = members.ToImmutable() });
        }
        return plan with { objects = objects.ToImmutable(), directOwnedFactories = factories.ToImmutable(), directBorrowedFactories = borrowedFactories.ToImmutable(), ownedProjectionOmissions = omissions.ToImmutable() };

        InteropFunction Native(int method) => exports[plan.methods[method].nativeExport];
        void Omit(
            ManagedMember member,
            string reason
        ) => omissions.Add(new(member.nativeExport, member.receiver!.parameter,
            exports[member.nativeExport].source, reason));
    }

    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified
        && !string.IsNullOrWhiteSpace(evidence.origin);
}
