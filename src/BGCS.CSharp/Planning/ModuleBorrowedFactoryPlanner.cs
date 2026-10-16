using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

internal static class ModuleBorrowedFactoryPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        ImmutableArray<CSharpModuleBorrowedFactoryPlan>.Builder factories = ImmutableArray.CreateBuilder<CSharpModuleBorrowedFactoryPlan>();
        var omissions = plan.ownedProjectionOmissions.ToBuilder();
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            InteropFunction function = functionsById[method.nativeExport].Single();
            if (function.returnString is not null || function.returnOwnership is not { kind: NativeOwnershipKind.Borrowed } borrowing) continue;
            string? restriction = Restriction(function, borrowing, method);
            if (restriction is null) factories.Add(new(index));
            else omissions.Add(new(function.id, null, function.source, restriction));
        }
        return plan with { directBorrowedFactories = factories.ToImmutable(), ownedProjectionOmissions = omissions.ToImmutable() };

        string? Restriction(
            InteropFunction function,
            OwnershipContract borrowing,
            CSharpMethodPlan method
        ) {
            if (function.error is not null || function.asynchronous is not null || function.returnString is not null || method.returnProjection is not null
                || borrowing.lifetime.kind != NativeLifetimeKind.Module || borrowing.lifetime.owner is not null
                || !Known(borrowing.evidence) || !Known(borrowing.lifetime.evidence)
                || borrowing.releaseFunction is not null || borrowing.allocator is not null || borrowing.releaseThreadDomain is not null)
                return "A module-borrowed result requires a synchronous pointer with explicit Module address-validity evidence and no native release, allocator or competing result protocol.";
            InteropType returned = plan.api.interop.GetType(function.returnType);
            while (returned.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                returned = plan.api.interop.GetType(returned.elementType!.Value);
            if (returned.category != NativeTypeCategory.Pointer || returned.elementType is not StableId)
                return "A module-borrowed result requires an actual native pointer; integer or value handles require an explicit adapter.";
            InteropType element = plan.api.interop.GetType(returned.elementType.Value);
            while (element.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                element = plan.api.interop.GetType(element.elementType!.Value);
            if (element.category == NativeTypeCategory.Function)
                return "A callback address is not a module-borrowed data object; retain its exact raw callback prototype instead.";
            if (!Enumerable.Range(0, function.parameters.Length).All(input => OverloadPlanner.SupportsDirectInput(plan, method, function, input)))
                return "A module-borrowed getter input lacks an implemented synchronous nonretaining friendly protocol; its raw import remains available.";
            return null;
        }
    }

    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified
        && !string.IsNullOrWhiteSpace(evidence.origin);
}
