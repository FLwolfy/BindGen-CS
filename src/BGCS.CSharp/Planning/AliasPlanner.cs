using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Planning;

internal static class AliasPlanner
{
    internal static ImmutableArray<CSharpAliasPlan> Plan(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        if (plan.api.aliases.IsDefault) throw new ArgumentException("Managed aliases must be initialized.");
        if (plan.api.aliases.IsEmpty) return [];
        var signatures = SharedContextPlanner.CreateMethods(plan with { aliases = [] }, request);
        ImmutableArray<CSharpAliasPlan>.Builder aliases = ImmutableArray.CreateBuilder<CSharpAliasPlan>();
        foreach (ManagedFunctionAlias alias in plan.api.aliases)
        {
            ArgumentNullException.ThrowIfNull(alias);
            int index = -1;
            for (int candidate = 0; candidate < plan.methods.Length; candidate++)
                if (plan.methods[candidate].nativeExport == alias.nativeExport) { index = candidate; break; }
            if (index < 0) throw new ArgumentException("A managed alias refers to an absent native operation.");
            string name = CSharpNames.Identifier(alias.name);
            CSharpMethodPlan target = plan.methods[index];
            InteropFunction native = plan.api.interop.functions.Single(function => function.id == target.nativeExport);
            bool customDocumentation = alias.documentation is not null || alias.documentationContent is not null;
            CSharpMethodPlan raw = target with
            {
                name = name,
                documentation = DocumentationPlaceholderPlanner.Fill(customDocumentation ? alias.documentation : target.documentation, plan.api.emitPlaceholderComments),
                documentationContent = DocumentationPlaceholderPlanner.FillContent(customDocumentation ? DocumentationPlanner.Plan(native with
                { documentation = alias.documentation, documentationContent = alias.documentationContent }) : target.documentationContent, plan.api.emitPlaceholderComments)
            };
            CSharpFacadeMethodPlan? friendly = signatures.SingleOrDefault(signature => signature.nativeExport == target.nativeExport);
            if (friendly is not null)
                friendly = friendly with
                {
                    name = name,
                    documentation = raw.documentation,
                    documentationContent = raw.documentationContent is { } content
                        ? content with { parameters = friendly.inputIndices.Select(input => content.parameters[input]).ToImmutableArray() } : null
                };
            aliases.Add(new(index, raw, friendly));
        }
        return aliases.ToImmutable();
    }
}
