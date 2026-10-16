using System;
using System.Collections.Immutable;
using System.Linq;

namespace BGCS.CSharp.Planning;

internal static class DocumentationPlaceholderPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        if (!plan.api.emitPlaceholderComments) return plan;
        return plan with
        {
            methods = plan.methods.Select(method => method with
            {
                documentation = Fill(method.documentation, true),
                documentationContent = FillContent(method.documentationContent, true)
            }).ToImmutableArray(),
            storage = plan.storage.Select(storage => storage with
            {
                documentation = Fill(storage.documentation, true),
                fields = storage.fields.Select(field => field with { documentation = Fill(field.documentation, true) }).ToImmutableArray(),
                bitfields = storage.bitfields.Select(field => field with { documentation = Fill(field.documentation, true) }).ToImmutableArray(),
                flexibleArrays = storage.flexibleArrays.Select(field => field with { documentation = Fill(field.documentation, true) }).ToImmutableArray(),
                enumValues = storage.enumValues.Select(value => value with { documentation = Fill(value.documentation, true) }).ToImmutableArray()
            }).ToImmutableArray(),
            managedEnums = plan.managedEnums.Select(enumeration => enumeration with
            {
                documentation = Fill(enumeration.documentation, true),
                values = enumeration.values.Select(value => value with { documentation = Fill(value.documentation, true) }).ToImmutableArray()
            }).ToImmutableArray(),
            constants = plan.constants.Select(constant => constant with { documentation = Fill(constant.documentation, true) }).ToImmutableArray(),
            stringConstants = plan.stringConstants.Select(constant => constant with { documentation = Fill(constant.documentation, true) }).ToImmutableArray()
        };
    }

    internal static string? Fill(
        string? source,
        bool enabled
    )
        => enabled && string.IsNullOrWhiteSpace(source) ? "To be documented." : source;

    internal static CSharpCallableDocumentationPlan? FillContent(
        CSharpCallableDocumentationPlan? source,
        bool enabled
    )
        => !enabled || source is null ? source : source with
        { summary = Fill(source.summary, true)!, parameters = source.parameters.Select(value => Fill(value, true)).ToImmutableArray() };
}
