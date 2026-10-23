using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Bridge;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Recipes;

/// <summary>Freezes declarative callable matching with deterministic conflict and exclusion rules.</summary>
internal sealed class CallableRecipeRegistry
{
    private readonly ImmutableArray<CallableLoweringRecipe> m_recipes;
    internal ImmutableArray<CallableLoweringRecipe> recipes => m_recipes;
    internal CallableRecipeRegistry(IEnumerable<CallableLoweringRecipe>? recipes)
    {
        m_recipes = (recipes ?? []).ToImmutableArray();
        foreach (CallableLoweringRecipe recipe in m_recipes)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentException.ThrowIfNullOrWhiteSpace(recipe.id);
            ArgumentException.ThrowIfNullOrWhiteSpace(recipe.functionPattern);
            if (recipe.sourceFragments.IsDefault) throw new ArgumentException("Callable helper collections must be initialized immutable arrays.", nameof(recipes));
            if (recipe.exportName is string name && (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || name.Contains("__", StringComparison.Ordinal)))
                throw new ArgumentException("A callable recipe export name must be an allocated unreserved C identifier.", nameof(recipes));
            if (recipe.invocationExpression is string expression && (string.IsNullOrWhiteSpace(expression) || expression.Split("{invocation}", StringSplitOptions.None).Length != 2))
                throw new ArgumentException("A callable recipe expression must wrap exactly one native invocation.", nameof(recipes));
            if (recipe.exclude && (recipe.exportName is not null || recipe.invocationExpression is not null || recipe.sourceFragments.Length != 0))
                throw new ArgumentException("An excluded callable cannot define an export, native invocation or helpers.", nameof(recipes));
        }
        if (m_recipes.Select(recipe => recipe.id).Distinct(StringComparer.Ordinal).Count() != m_recipes.Length)
            throw new InvalidOperationException("A callable recipe identity is registered more than once.");
        m_recipes = m_recipes.OrderByDescending(recipe => recipe.priority).ThenBy(recipe => recipe.id, StringComparer.Ordinal).ToImmutableArray();
    }

    internal CallableLoweringRecipe? Match(NativeDeclaration declaration)
    {
        foreach (var tier in m_recipes.GroupBy(recipe => recipe.priority))
        {
            CallableLoweringRecipe[] matches = tier.Where(recipe => LoweringRecipeCompiler.Matches(declaration.qualifiedName, recipe.functionPattern)
                || LoweringRecipeCompiler.Matches(declaration.name, recipe.functionPattern)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Exclusive callable recipes conflict: " + string.Join(", ", matches.Select(recipe => recipe.id)));
            if (matches.Length == 1) return matches[0];
        }
        return null;
    }

    internal static BridgeExport Apply(
        BridgeExport export,
        CallableLoweringRecipe? recipe
    ) => recipe is null ? export : export with
    {
        name = recipe.exportName ?? export.name,
        operation = export.operation with
        {
            invocationExpression = recipe.invocationExpression is null ? export.operation.invocationExpression
            : LoweringRecipeCompiler.Expression(recipe.id, recipe.invocationExpression)
        }
    };
}
