using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Planning;

internal static class FunctionContainerPlanner
{
    internal static ImmutableArray<CSharpFunctionContainerPlan> Plan(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        if (plan.api.functionContainers.IsDefault) throw new ArgumentException("Function containers must be initialized.");
        if (plan.api.functionContainers.IsEmpty) return [];
        var signatures = SharedContextPlanner.CreateMethods(plan with { aliases = [] }, request);
        ImmutableArray<CSharpFunctionContainerPlan>.Builder result = ImmutableArray.CreateBuilder<CSharpFunctionContainerPlan>();
        foreach (var container in plan.api.functionContainers)
        {
            ArgumentNullException.ThrowIfNull(container);
            string name = CSharpNames.Identifier(container.name);
            if (container.methods.IsDefaultOrEmpty) throw new ArgumentException("A function container requires explicit existing operations.");
            ImmutableArray<CSharpContainerMethodPlan>.Builder methods = ImmutableArray.CreateBuilder<CSharpContainerMethodPlan>();
            foreach (var selected in container.methods)
            {
                CSharpFacadeMethodPlan original = signatures.SingleOrDefault(method => method.nativeExport == selected.nativeExport)
                    ?? throw new NotSupportedException("A named function container requires an existing checked friendly operation: " + selected.nativeExport.value);
                if (selected.documentation is not null || selected.documentationContent is not null)
                {
                    var native = plan.api.interop.functions.Single(function => function.id == selected.nativeExport);
                    var content = DocumentationPlaceholderPlanner.FillContent(DocumentationPlanner.Plan(native with
                    { documentation = selected.documentation, documentationContent = selected.documentationContent }), plan.api.emitPlaceholderComments);
                    original = original with
                    {
                        documentation = DocumentationPlaceholderPlanner.Fill(selected.documentation, plan.api.emitPlaceholderComments),
                        documentationContent = content is null ? null : content with { parameters = original.inputIndices.Select(index => content.parameters[index]).ToImmutableArray() }
                    };
                }
                methods.Add(new(original.name, original with { name = CSharpNames.Identifier(selected.name) }));
            }
            result.Add(new(name, plan.contextName.TrimStart('@') + name.TrimStart('@') + "Functions", methods.ToImmutable()));
        }
        if (result.Select(container => container.name.TrimStart('@')).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new ArgumentException("Function container names are duplicated.");
        string[] reservedTypes = plan.storage.Select(type => type.name.TrimStart('@')).Concat(plan.managedEnums.Select(type => type.name.TrimStart('@')))
            .Append(plan.contextName.TrimStart('@')).Append(plan.api.name.TrimStart('@')).ToArray();
        if (result.Any(container => reservedTypes.Contains(container.viewType, StringComparer.Ordinal)))
            throw new ArgumentException("A function container view type collides with a generated storage or API type.");
        foreach (CSharpFunctionContainerPlan container in result)
        {
            string[] signaturesInView = container.methods.Select(method => method.signature.name.TrimStart('@') + "(" + string.Join(',', method.signature.parameterTypes.Select(type => type.TrimEnd('?'))) + ")").ToArray();
            if (signaturesInView.Distinct(StringComparer.Ordinal).Count() != signaturesInView.Length
                || container.methods.Any(method => method.signature.name.TrimStart('@') is "Current" or "m_context" || method.signature.name.TrimStart('@') == container.viewType))
                throw new ArgumentException("A function container method collides with another view member.");
        }
        return result.ToImmutable();
    }
}

/// <summary>Plans an allocation-free context-bound view sharing checked conversions and native leases.</summary>
/// <param name="name">The context property name.</param>
/// <param name="viewType">The public readonly view type.</param>
/// <param name="methods">Exact friendly forwarding signatures.</param>
public sealed record CSharpFunctionContainerPlan(
    string name,
    string viewType,
    ImmutableArray<CSharpContainerMethodPlan> methods
);

/// <summary>Plans a renamed method within a context-bound function view.</summary>
/// <param name="targetName">The original checked context method.</param>
/// <param name="signature">The view-local method signature.</param>
public sealed record CSharpContainerMethodPlan(
    string targetName,
    CSharpFacadeMethodPlan signature
)
{
    /// <summary>Gets explicit forwarded constants and arguments, or an empty array for ordinary parameter forwarding.</summary>
    public ImmutableArray<string> arguments { get; init; } = [];
}
