using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;

namespace BGCS.CSharp.Projection;

internal static class ManagedFunctionCustomization
{
    internal static ImmutableArray<ManagedMember> Apply(
        ImmutableArray<ManagedMember> methods,
        ImmutableArray<ManagedFunctionRuleRequest> rules,
        Func<string, InteropFunction> resolve
    ) {
        if (rules.IsDefault) throw new ArgumentException("Managed function rules must be initialized.");
        HashSet<StableId> customized = [];
        foreach (ManagedFunctionRuleRequest rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            InteropFunction function = resolve(rule.function);
            if (!customized.Add(function.id)) throw new ArgumentException("A native operation has multiple presentation rules.");
            ManagedMember member = methods.Single(member => member.nativeExport == function.id);
            if (rule.defaults.IsDefault || rule.parameters.IsDefault || rule.inputVariations.IsDefault) throw new ArgumentException("Function presentation choices must be initialized.");
            ImmutableArray<ManagedInputVariation> variations = rule.inputVariations.Select(variation =>
            {
                ArgumentNullException.ThrowIfNull(variation);
                if (variation.parameters.IsDefaultOrEmpty) throw new ArgumentException("An input variation requires at least one explicit input conversion.");
                return new ManagedInputVariation(variation.parameters.Select(parameter =>
                {
                    ArgumentNullException.ThrowIfNull(parameter);
                    ArgumentNullException.ThrowIfNull(parameter.projection);
                    ArgumentException.ThrowIfNullOrWhiteSpace(parameter.projection.managedToNativeExpression);
                    return new ManagedValueProjection(Parameter(parameter.parameter).id, parameter.projection);
                }).ToImmutableArray())
                { name = variation.name };
            }).ToImmutableArray();
            ImmutableArray<ManagedDefaultArgument> defaults = rule.defaults.Select(argument =>
            {
                ArgumentNullException.ThrowIfNull(argument);
                ArgumentNullException.ThrowIfNull(argument.value);
                return new ManagedDefaultArgument(Parameter(argument.parameter).id, argument.value);
            }).ToImmutableArray();
            if (defaults.Select(argument => argument.nativeParameter).Distinct().Count() != defaults.Length)
                throw new ArgumentException("A native parameter has multiple default arguments.");
            var values = member.valueProjections.ToBuilder();
            foreach (ManagedParameterProjectionRequest parameter in rule.parameters)
            {
                ArgumentNullException.ThrowIfNull(parameter);
                ArgumentNullException.ThrowIfNull(parameter.projection);
                ArgumentException.ThrowIfNullOrWhiteSpace(parameter.projection.managedToNativeExpression);
                Add(Parameter(parameter.parameter).id, parameter.projection);
            }
            ManagedRecipeProjection? returned = member.returnProjection;
            if (rule.returns is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(rule.returns.nativeToManagedExpression);
                if (function.error is not null)
                {
                    if (function.error.valueParameter is not StableId result)
                        throw new ArgumentException("A status operation without an original result cannot declare a friendly return conversion.");
                    Add(result, rule.returns);
                }
                else
                {
                    if (returned is not null) throw new ArgumentException("A native return has multiple explicit friendly conversions.");
                    returned = rule.returns;
                }
            }
            methods = methods.SetItem(methods.IndexOf(member), member with
            {
                name = rule.name ?? member.name,
                documentationOverride = rule.documentation,
                defaults = defaults,
                inputVariations = variations,
                valueProjections = values.ToImmutable(),
                returnProjection = returned
            });

            InteropParameter Parameter(string selector)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(selector);
                InteropParameter[] selected = function.parameters.Where(parameter => parameter.id.value == selector
                    || parameter.sourceName == selector || parameter.name == selector).ToArray();
                if (selected.Length != 1) throw new ArgumentException("A presentation rule must identify exactly one native parameter: " + selector);
                return selected[0];
            }
            void Add(
                StableId identity,
                ManagedRecipeProjection projection
            ) {
                if (values.Any(value => value.nativeParameter == identity))
                    throw new ArgumentException("A native parameter has multiple explicit friendly conversions.");
                values.Add(new(identity, projection));
            }
        }
        return methods;
    }
}
