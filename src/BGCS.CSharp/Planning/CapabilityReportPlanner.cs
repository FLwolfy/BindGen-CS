using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Capabilities;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;

namespace BGCS.CSharp.Planning;

internal static class CapabilityReportPlanner
{
    internal static ImmutableArray<BindingCapabilityReport> Plan(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        ManagedRuntimeProfile runtime = plan.managedRuntime ?? throw new InvalidOperationException("Capability reporting requires the selected execution profile.");
        ContentDigest profileDigest = ManagedRuntimeProfileIdentity.Capture(runtime);
        HashSet<StableId> friendly = SharedContextPlanner.CreateMethods(plan, request)
            .Where(method => method.nativeExport is not null).Select(method => method.nativeExport!.Value).ToHashSet();
        foreach (CSharpObjectPlan instance in plan.objects)
            foreach (int index in instance.methodIndices) friendly.Add(plan.methods[plan.statusMethods[index].methodIndex].nativeExport);
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
        {
            friendly.Add(plan.methods[proxy.create.methodIndex].nativeExport);
            friendly.Add(plan.methods[proxy.destroy.methodIndex].nativeExport);
            friendly.Add(plan.methods[proxy.cast.methodIndex].nativeExport);
        }
        Dictionary<StableId, BindingCapabilityReport> reports = [];
        Dictionary<StableId, InteropFunction> exports = plan.api.interop.functions.ToDictionary(function => function.id);
        foreach (CSharpMethodPlan method in plan.methods)
        {
            InteropFunction function = exports[method.nativeExport];
            string[] reasons = plan.callbackOmissions.Where(omission => omission.nativeExport == function.id).Select(omission => omission.reason)
                .Concat(plan.ownedProjectionOmissions.Where(omission => omission.nativeExport == function.id).Select(omission => omission.reason))
                .Concat(plan.valueParameterOmissions.Where(omission => omission.nativeExport == function.id).Select(omission => omission.reason))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!friendly.Contains(function.id) && reasons.Length == 0) reasons = ["No friendly projection was planned for this declaration."];
            Add(function.id, function.exportName, friendly.Contains(function.id) ? DeclarationGenerationStatus.GeneratedFriendly : DeclarationGenerationStatus.GeneratedRaw,
                reasons.ToImmutableArray(), function.source);
            reports[function.id] = reports[function.id] with
            {
                consumerRequirements = function.parameters
                .Where(parameter => parameter.callback?.requiredResultOutputIndex is not null)
                .Select(parameter => "The raw callback must fully initialize its normalized result-output argument "
                    + parameter.callback!.requiredResultOutputIndex!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " on every invocation, including failure paths.")
                .ToImmutableArray()
            };
        }
        foreach (CSharpStoragePlan storage in plan.storage)
        {
            var type = plan.api.interop.GetType(storage.nativeType);
            Add(type.id, type.nativeName, DeclarationGenerationStatus.GeneratedRaw, [], type.source);
        }
        foreach (CSharpTypePlan carrier in plan.types)
        {
            InteropType type = plan.api.interop.GetType(carrier.nativeType);
            if (type.category != NativeTypeCategory.Alias || reports.ContainsKey(type.id)) continue;
            InteropType resolved = type;
            HashSet<StableId> visited = [];
            while (resolved.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(resolved.id) || resolved.elementType is not StableId element)
                    throw new InvalidOperationException("A capability report encountered an incomplete native alias graph.");
                resolved = plan.api.interop.GetType(element);
            }
            if (resolved.category == NativeTypeCategory.Pointer && resolved.elementType is StableId pointee)
            {
                InteropType pointed = plan.api.interop.GetType(pointee);
                while (pointed.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                    pointed = plan.api.interop.GetType(pointed.elementType!.Value);
                if (pointed.category == NativeTypeCategory.Function) resolved = pointed;
            }
            CSharpOpaqueCallablePlan? opaque = plan.opaqueCallables.FirstOrDefault(item => item.nativeType == resolved.id);
            Add(type.id, type.nativeName, opaque is null ? DeclarationGenerationStatus.GeneratedRaw : DeclarationGenerationStatus.Unsupported,
                opaque is null ? ["The native typedef is represented by the planned raw carrier " + carrier.syntax + "."] : [opaque.reason], type.source);
        }
        foreach (CSharpAbiDelegatePlan declaration in plan.abiDelegates)
        {
            var type = plan.api.interop.GetType(declaration.nativeType);
            Add(type.id, type.nativeName, DeclarationGenerationStatus.GeneratedRaw,
                ["A typed delegate declaration does not establish callback registration lifetime."], type.source);
        }
        foreach (CSharpOpaqueCallablePlan opaque in plan.opaqueCallables)
        {
            var type = plan.api.interop.GetType(opaque.nativeType);
            Add(type.id, type.nativeName, DeclarationGenerationStatus.Unsupported, [opaque.reason], type.source);
        }
        foreach (CSharpVirtualProxyOmissionPlan omission in plan.virtualProxyOmissions)
            Add(omission.proxy, omission.proxy.value, DeclarationGenerationStatus.Unsupported, [omission.reason], omission.source);
        foreach (CSharpConstantPlan constant in plan.constants)
            Add(constant.sourceDeclaration, constant.name, DeclarationGenerationStatus.GeneratedRaw, [], null);
        foreach (CSharpStringConstantPlan constant in plan.stringConstants)
            Add(constant.sourceDeclaration, constant.name, DeclarationGenerationStatus.GeneratedRaw, [], null);
        return reports.Values.OrderBy(report => report.declarationId.value, StringComparer.Ordinal).ToImmutableArray();

        void Add(
            StableId id,
            string name,
            DeclarationGenerationStatus status,
            ImmutableArray<string> reasons,
            SourceLocation? source
        )
            => reports[id] = new(id, name, plan.api.interop.targetId, runtime.runtimeId, runtime.id, status, reasons)
            { source = source, callAbi = runtime.callAbi, runtimeProfileDigest = profileDigest };
    }
}
