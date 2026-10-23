using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Declarations;
using BGCS.Core.Bridge;
using BGCS.Core.Capabilities;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS;

internal static class CapabilityReportStage
{
    internal static ImmutableArray<BindingCapabilityReport> Merge(
        NativeModule original,
        DeclarationSelection selection,
        InteropModule interop,
        NativeBridgePlan? bridge,
        ManagedRuntimeProfile? runtime,
        ImmutableArray<BindingCapabilityReport> emitted,
        bool bridgeOnly
    ) {
        ContentDigest? profileDigest = runtime is null ? null : ManagedRuntimeProfileIdentity.Capture(runtime);
        Dictionary<StableId, BindingCapabilityReport> reports = [];
        Dictionary<StableId, InteropFunction> functions = interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, InteropType> types = interop.types.ToDictionary(type => type.id);
        Dictionary<StableId, StableId?> originalTypeDeclarations = original.types.ToDictionary(type => type.id, type => type.declaration);
        Dictionary<StableId, VirtualProxyContract> proxyDeclarations = [];
        foreach (VirtualProxyContract proxy in interop.virtualProxies)
            foreach (StableId id in new[] { proxy.id, proxy.createFunction, proxy.destroyFunction, proxy.interfaceCastFunction })
                proxyDeclarations.Add(id, proxy);
        foreach (BindingCapabilityReport report in emitted)
        {
            functions.TryGetValue(report.declarationId, out InteropFunction? function);
            types.TryGetValue(report.declarationId, out InteropType? type);
            proxyDeclarations.TryGetValue(report.declarationId, out VirtualProxyContract? proxy);
            originalTypeDeclarations.TryGetValue(report.declarationId, out StableId? originalDeclaration);
            StableId? proxyDeclaration = proxy is null ? null : original.GetType(proxy.sourceInterface).declaration;
            StableId logical = proxyDeclaration ?? function?.sourceDeclaration ?? type?.sourceDeclaration
                ?? originalDeclaration ?? report.declarationId;
            BindingCapabilityReport mapped = report with
            {
                declarationId = logical,
                nativeName = proxy?.sourceQualifiedName ?? function?.sourceQualifiedName ?? function?.sourceName ?? type?.sourceQualifiedName ?? report.nativeName,
                consumerRequirements = bridge is null ? report.consumerRequirements
                    : report.consumerRequirements.Add("Compile the declared native bridge sources into the binding module using the consumer project's own build process.")
            };
            if (reports.TryGetValue(logical, out BindingCapabilityReport? previous))
                mapped = mapped with
                {
                    status = Strength(previous.status) > Strength(mapped.status) ? previous.status : mapped.status,
                    reasons = previous.reasons.Concat(mapped.reasons).Distinct(StringComparer.Ordinal).ToImmutableArray(),
                    consumerRequirements = previous.consumerRequirements.Concat(mapped.consumerRequirements).Distinct(StringComparer.Ordinal).ToImmutableArray()
                };
            reports[logical] = mapped;
        }
        if (bridgeOnly && bridge is not null)
        {
            foreach (BridgeExport export in bridge.exports.Where(export => export.operation.declaration is not null))
            {
                NativeDeclaration declaration = original.GetDeclaration(export.operation.declaration!.Value);
                reports[declaration.id] = New(declaration, DeclarationGenerationStatus.GeneratedNativeBridge,
                    ["Native source and export contracts were generated; managed binding emission was not requested."]);
            }
            foreach (CppVirtualProxyPlan proxy in bridge.virtualProxies)
            {
                NativeDeclaration declaration = original.GetDeclaration(original.GetType(proxy.nativeInterface).declaration!.Value);
                reports[declaration.id] = New(declaration, DeclarationGenerationStatus.GeneratedNativeBridge,
                    ["Native proxy source and export contracts were generated; managed binding emission was not requested."]);
            }
        }
        HashSet<StableId> roots = selection.publicDeclarations.ToHashSet();
        foreach (NativeDeclaration declaration in original.declarations.Where(declaration => !declaration.isSystemDeclaration
            && declaration.kind is "Function" or "Class" or "Enum" or "Typedef"))
        {
            if (reports.ContainsKey(declaration.id)) continue;
            if (!roots.Contains(declaration.id))
                reports[declaration.id] = New(declaration, DeclarationGenerationStatus.Excluded,
                    ["The declaration was not selected as a public generation root."]);
            else
                reports[declaration.id] = New(declaration, DeclarationGenerationStatus.Unsupported,
                    [bridgeOnly ? "The declaration has no independent native bridge export."
                        : "The selected backend did not report an emitted representation for this selected declaration."]);
        }
        return reports.Values.OrderBy(report => report.declarationId.value, StringComparer.Ordinal).ToImmutableArray();

        BindingCapabilityReport New(
            NativeDeclaration declaration,
            DeclarationGenerationStatus status,
            ImmutableArray<string> reasons
        )
            => new(declaration.id, declaration.qualifiedName, original.targetId, runtime?.runtimeId, runtime?.id, status, reasons)
            {
                source = declaration.source,
                callAbi = runtime?.callAbi,
                runtimeProfileDigest = profileDigest
            };
        static int Strength(DeclarationGenerationStatus status) => status switch
        {
            DeclarationGenerationStatus.GeneratedFriendly => 3,
            DeclarationGenerationStatus.GeneratedRaw => 2,
            DeclarationGenerationStatus.GeneratedNativeBridge => 1,
            _ => 0
        };
    }
}
