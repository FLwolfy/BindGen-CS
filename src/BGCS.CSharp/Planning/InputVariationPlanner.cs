using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Projection;

namespace BGCS.CSharp.Planning;

/// <summary>Plans a finite alternate managed input signature without changing the raw ABI or duplicating lifecycle code.</summary>
/// <param name="signature">The public alternate signature.</param>
/// <param name="targetName">The existing checked operation.</param>
/// <param name="arguments">Ordered value conversions and exact reference forwarding.</param>
public sealed record CSharpInputVariationPlan(
    CSharpFacadeMethodPlan signature,
    string targetName,
    ImmutableArray<string> arguments
);

internal static class InputVariationPlanner
{
    internal static CSharpEmissionPlan Apply(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        var variations = ImmutableArray.CreateBuilder<CSharpInputVariationPlan>();
        var conversions = plan.recipeProjections.ToDictionary(projection => projection.id, StringComparer.Ordinal);
        var bases = SharedContextPlanner.CreateMethods(plan with { aliases = [], defaultOverloads = [], inputVariations = [] }, request);
        foreach (ManagedMember member in plan.api.methods)
        {
            if (member.inputVariations.IsDefault) throw new ArgumentException("Managed input variations must be initialized.");
            if (member.inputVariations.IsEmpty) continue;
            CSharpFacadeMethodPlan[] candidates = bases.Where(method => method.nativeExport == member.nativeExport && method.name == member.name).ToArray();
            if (candidates.Length != 1) throw new NotSupportedException("An input variation requires one unambiguous existing checked friendly operation.");
            CSharpFacadeMethodPlan target = candidates[0];
            var native = plan.api.interop.functions.Single(function => function.id == member.nativeExport);
            foreach (ManagedInputVariation variation in member.inputVariations)
            {
                ArgumentNullException.ThrowIfNull(variation);
                if (variation.parameters.IsDefaultOrEmpty) throw new ArgumentException("An input variation requires explicit value conversions.");
                var types = target.parameterTypes.ToBuilder();
                var arguments = Enumerable.Range(0, target.parameterTypes.Length).Select(index => FacadeParameters.Argument(target, index)).ToArray();
                HashSet<StableId> selected = [];
                foreach (ManagedValueProjection input in variation.parameters)
                {
                    if (!selected.Add(input.nativeParameter)) throw new ArgumentException("An input variation converts the same parameter more than once.");
                    int nativeIndex = -1;
                    for (int index = 0; index < native.parameters.Length; index++) if (native.parameters[index].id == input.nativeParameter) { nativeIndex = index; break; }
                    int publicIndex = target.inputIndices.IndexOf(nativeIndex);
                    if (nativeIndex < 0 || publicIndex < 0) throw new ArgumentException("An input variation selects an absent or hidden native parameter.");
                    if (!target.parameterModifiers.IsDefaultOrEmpty && target.parameterModifiers[publicIndex].Length != 0)
                        throw new NotSupportedException("An alternate value conversion cannot replace an in/ref/out borrow; keep the exact reference or declare a native adapter.");
                    CSharpRecipeProjectionPlan conversion = new ManagedRecipeProjectionValidator().Plan(input.projection, target.parameterTypes[publicIndex], plan.api.interop.pointerSize);
                    if (conversion.toNativeExpression is null) throw new ArgumentException("An input variation requires an explicit conversion into the existing friendly input.");
                    conversions.TryAdd(conversion.id, conversion);
                    types[publicIndex] = conversion.managedType;
                    arguments[publicIndex] = "RecipeConversions.__ToNative_" + conversion.id + "(" + target.parameterNames[publicIndex] + ")";
                }
                variations.Add(new(target with { name = variation.name is null ? target.name : CSharpNames.Identifier(variation.name), parameterTypes = types.ToImmutable() }, target.name, arguments.ToImmutableArray()));
                if (variations.Count + plan.defaultOverloads.Length > plan.api.defaultOverloadLimit)
                    throw new ArgumentException("Combined managed input variations and default overloads exceed the configured finite overload limit.");
            }
        }
        return plan with { inputVariations = variations.ToImmutable(), recipeProjections = conversions.Values.OrderBy(projection => projection.id, StringComparer.Ordinal).ToImmutableArray() };
    }
}
