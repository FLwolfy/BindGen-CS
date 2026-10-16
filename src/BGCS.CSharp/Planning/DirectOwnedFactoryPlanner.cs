using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

internal static class DirectOwnedFactoryPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        ImmutableArray<CSharpDirectOwnedFactoryPlan>.Builder factories = ImmutableArray.CreateBuilder<CSharpDirectOwnedFactoryPlan>();
        var omissions = plan.ownedProjectionOmissions.ToBuilder();
        Dictionary<StableId, InteropFunction> exports = plan.api.interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, int> importIndices = plan.methods.Select((
            method,
            index
        ) => (method.nativeExport, index)).ToDictionary(item => item.nativeExport, item => item.index);
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            InteropFunction function = exports[method.nativeExport];
            if (function.returnString is not null || function.returnOwnership is not { kind: NativeOwnershipKind.Owned }) continue;
            try { factories.Add(Plan(index, method, function)); }
            catch (NotSupportedException failure) { omissions.Add(new(function.id, null, function.source, failure.Message)); }
        }
        return plan with { directOwnedFactories = factories.ToImmutable(), ownedProjectionOmissions = omissions.ToImmutable() };

        CSharpDirectOwnedFactoryPlan Plan(
            int index,
            CSharpMethodPlan method,
            InteropFunction function
        ) {
            OwnershipContract owned = function.returnOwnership!;
            if (function.error is not null || function.asynchronous is not null || function.returnString is not null || method.returnProjection is not null
                || owned is not { kind: NativeOwnershipKind.Owned, releaseFunction: StableId releaseId, allocator: StableId allocatorId, releaseThreadDomain: "any" }
                || owned.lifetime.kind != NativeLifetimeKind.Owner || owned.lifetime.owner != function.id || !Known(owned.evidence) || !Known(owned.lifetime.evidence))
                throw new NotSupportedException("A direct owned factory requires synchronous pointer ownership, an explicit owner/release domain and any-thread cleanup evidence.");
            InteropType returned = Resolve(function.returnType);
            if (returned.category != NativeTypeCategory.Pointer || returned.elementType is not StableId)
                throw new NotSupportedException("The implemented direct owned factory requires one native pointer return; integer and special value handles need an explicit adapter.");
            if (!importIndices.TryGetValue(releaseId, out int releaseIndex) || releaseIndex == index) throw new NotSupportedException("A direct owned factory requires a selected independent matching release export.");
            InteropFunction release = exports[releaseId];
            if (release.error is not null || release.asynchronous is not null || release.returnOwnership is not null || release.returnString is not null
                || release.parameters.Length != 1 || Resolve(release.returnType).primitiveKind != "Void" || Resolve(release.parameters[0].type).id != returned.id
                || release.parameters[0].ownership is not null || release.parameters[0].callback is not null || release.parameters[0].buffer is not null
                || release.parameters[0].singleValue is not null || release.parameters[0].text is not null)
                throw new NotSupportedException("The implemented direct owned factory requires the exact synchronous void(T*) release ABI without a conflicting release parameter protocol.");
            if (!Enumerable.Range(0, function.parameters.Length).All(input => OverloadPlanner.SupportsDirectInput(plan, method, function, input)))
                throw new NotSupportedException("A direct owned factory input lacks an implemented nonretaining friendly protocol; the raw factory remains available.");
            return new(index, new(releaseIndex, allocatorId.value, -1, -1, 0));
        }
        InteropType Resolve(StableId id)
        {
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = plan.api.interop.GetType(type.elementType!.Value);
            return type;
        }
    }

    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified
        && !string.IsNullOrWhiteSpace(evidence.origin);
}
