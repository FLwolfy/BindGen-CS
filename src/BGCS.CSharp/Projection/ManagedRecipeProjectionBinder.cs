using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Analysis.Bridge;

namespace BGCS.CSharp.Projection;

/// <summary>Associates detached bridge conversion assertions with exact final C ABI parameter identities.</summary>
public sealed class ManagedRecipeProjectionBinder
{
    /// <summary>Preserves exact raw carriers while adding explicit presentation metadata and all configured helper requests.</summary>
    /// <param name="model">The ordinary managed projection over the final verified ABI.</param>
    /// <param name="bridge">The typed bridge that supplied original native conversion metadata.</param>
    /// <param name="recipes">All configured recipes, including unmatched helper declarations.</param>
    /// <returns>A new immutable managed model with exact input and original-result conversion associations.</returns>
    public ManagedApiModel Apply(
        ManagedApiModel model,
        NativeBridgePlan bridge,
        ImmutableArray<TypeLoweringRecipe> recipes
    ) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bridge);
        new ManagedRecipeProjectionValidator().ValidateRecipes(recipes);
        if (bridge.source.language == BGCS.Core.Inputs.SourceLanguage.C) return BindC(model, bridge, recipes);
        var exports = bridge.exports.ToDictionary(export => export.id);
        var functions = model.interop.functions.ToDictionary(function => function.id);
        var bridgeTypes = bridge.types.ToDictionary(type => type.id);
        return model with
        {
            recipeHelpers = model.recipeHelpers.AddRange(recipes.Where(recipe => recipe.managedProjection is not null)
                .Select(recipe => new ManagedRecipeHelper(recipe.id, recipe.cAbiType, recipe.managedProjection!))),
            methods = model.methods.Select(member =>
            {
                if (!exports.TryGetValue(member.nativeExport, out BridgeExport? export)) return member;
                if (!functions.TryGetValue(member.nativeExport, out var function))
                    throw new InvalidOperationException("A managed native recipe member lost its exact verified ABI export identity.");
                ImmutableArray<ManagedValueProjection>.Builder values = member.valueProjections.ToBuilder();
                foreach (BridgeParameter parameter in export.parameters.Where(parameter => parameter.managedProjection is not null))
                {
                    if (!function.parameters.Any(candidate => candidate.id == parameter.id)) throw new InvalidOperationException("A managed native recipe input lost its exact verified ABI parameter identity.");
                    if (values.Any(value => value.nativeParameter == parameter.id)) throw new InvalidOperationException("A managed recipe input projection is registered more than once.");
                    values.Add(new(parameter.id, parameter.managedProjection!));
                }
                if (export.managedResultProjection is ManagedRecipeProjection projection)
                {
                    StableId result = new("bridge-result:" + export.id.value);
                    if (function.error?.valueParameter != result || !function.parameters.Any(parameter => parameter.id == result))
                        throw new InvalidOperationException("A managed native recipe result lost its verified original-result channel.");
                    if (values.Any(value => value.nativeParameter == result)) throw new InvalidOperationException("A managed recipe result projection is registered more than once.");
                    values.Add(new(result, projection));
                }
                foreach (BridgeParameter parameter in export.parameters)
                {
                    if (parameter.conversion != BridgeConversionKind.ScalarCast || parameter.nativeType is not StableId sourceParameterType || !Boolean(sourceParameterType, parameter.type)
                        || values.Any(value => value.nativeParameter == parameter.id)) continue;
                    values.Add(new(parameter.id, new("bool", "{value} ? (byte)1 : (byte)0", null)));
                }
                if (export.resultConversion == BridgeConversionKind.ScalarCast && export.nativeResultType is StableId nativeResult
                    && export.resultType is StableId carrier && Boolean(nativeResult, carrier)
                    && function.error?.valueParameter is StableId resultId && !values.Any(value => value.nativeParameter == resultId))
                    values.Add(new(resultId, new("bool", null, "{value} != 0")));
                return member with { valueProjections = values.ToImmutable() };
            }).ToImmutableArray()
        };

        bool Boolean(
            StableId native,
            StableId carrier
        ) {
            NativeType source = StandardLibraryFactReader.Resolve(bridge.source, native);
            if (!bridgeTypes.TryGetValue(carrier, out BridgeType? representation))
                throw new InvalidOperationException("A managed native boolean projection lost its exact verified bridge carrier identity.");
            return source.category == NativeTypeCategory.Primitive && source.primitiveKind == "Bool" && source.size == 1
                && representation.kind == BridgeTypeKind.Scalar && representation.cName == "uint8_t";
        }
    }
    private static ManagedApiModel BindC(
        ManagedApiModel model,
        NativeBridgePlan bridge,
        ImmutableArray<TypeLoweringRecipe> recipes
    ) {
        var exports = bridge.exports.ToDictionary(export => export.operation.finiteVariadic is null ? export.operation.declaration!.Value : export.id);
        var functions = model.interop.functions.ToDictionary(function => function.id);
        return model with
        {
            recipeHelpers = model.recipeHelpers.AddRange(recipes.Where(recipe => recipe.managedProjection is not null)
                .Select(recipe => new ManagedRecipeHelper(recipe.id, recipe.cAbiType, recipe.managedProjection!))),
            methods = model.methods.Select(member =>
            {
                if (!exports.TryGetValue(member.nativeExport, out BridgeExport? export)) return member;
                var function = functions[member.nativeExport];
                var values = member.valueProjections.ToBuilder();
                foreach (BridgeParameter parameter in export.parameters.Where(parameter => parameter.managedProjection is not null))
                {
                    StableId original = export.operation.finiteVariadic is not null ? parameter.id : export.operation.argumentGroups.IsEmpty
                        ? bridge.source.GetDeclaration(export.operation.declaration!.Value).children.Select(bridge.source.GetDeclaration)
                            .Where(declaration => declaration.kind == "Parameter").ElementAt(export.parameters.IndexOf(parameter)).id
                        : export.operation.argumentGroups.Single(group => group.inputs.Any(input => input.parameter == parameter.id)).sourceParameter;
                    var candidates = function.parameters.Where(candidate => candidate.sourceDeclaration == original || candidate.id == original).ToArray();
                    if (candidates.Length != 1 || values.Any(value => value.nativeParameter == candidates[0].id))
                        throw new InvalidOperationException("A C managed value projection must identify one exact unexpanded physical input.");
                    values.Add(new(candidates[0].id, parameter.managedProjection!));
                }
                if (export.managedResultProjection is not null && export.resultInOutputParameter)
                    throw new NotSupportedException("A C managed aggregate result projection requires its explicit output-channel projection rather than an inferred return conversion.");
                if (export.managedResultProjection is not null && member.returnProjection is not null)
                    throw new InvalidOperationException("A C managed direct-return projection was registered more than once.");
                return member with { valueProjections = values.ToImmutable(), returnProjection = export.managedResultProjection ?? member.returnProjection };
            }).ToImmutableArray()
        };
    }
}
