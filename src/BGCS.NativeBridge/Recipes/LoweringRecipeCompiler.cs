using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.NativeBridge.Recipes;

/// <summary>Compiles immutable native conversion recipes into the same neutral lowering port as plugins.</summary>
public sealed class LoweringRecipeCompiler
{
    /// <summary>Creates a detached type lowering with a complete fixed-width carrier graph.</summary>
    /// <param name="recipe">The native representation and explicitly asserted conversion expressions.</param>
    /// <returns>An independently owned recipe lowering suitable for one generation session.</returns>
    public INativeTypeLowering Compile(TypeLoweringRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.id);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.typePattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.cAbiType);
        if (recipe.sourceFragments.IsDefault) throw new ArgumentException("Recipe native helper collections must be initialized immutable arrays.", nameof(recipe));
        if (recipe.abiParameters.IsDefault) throw new ArgumentException("Recipe ABI input collections must be initialized immutable arrays.", nameof(recipe));
        if (recipe.parameterExpression is not null) ArgumentException.ThrowIfNullOrWhiteSpace(recipe.parameterExpression);
        if (recipe.resultExpression is not null) ArgumentException.ThrowIfNullOrWhiteSpace(recipe.resultExpression);
        Carrier(recipe.cAbiType, 8);
        if (recipe.ownedResultRelease is CppResultReleaseRequest release)
        {
            ValidateRelease(release);
            var result = Carrier(recipe.cAbiType, 8);
            if (result.Types.Single(type => type.id == result.Type) is not { kind: BridgeTypeKind.Pointer, isConst: false })
                throw new NotSupportedException("An owned native recipe requires an exact nonconst C pointer carrier.");
            if (recipe.resultExpression is null)
                throw new ArgumentException("An owned native recipe must explicitly declare its result conversion and allocation.", nameof(recipe));
            if (recipe.managedProjection?.nativeToManagedExpression is not null)
                throw new NotSupportedException("An owning result cannot also use an unrelated single-value managed result expression.");
        }
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (RecipeAbiParameter parameter in recipe.abiParameters)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentException.ThrowIfNullOrWhiteSpace(parameter.name);
            if (!Regex.IsMatch(parameter.name, "^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)
                || parameter.name == "nativeType" || !names.Add(parameter.name))
                throw new ArgumentException("Recipe ABI inputs need unique local placeholder names excluding nativeType.", nameof(recipe));
            Carrier(parameter.cAbiType, 8);
        }
        if (!recipe.abiParameters.IsEmpty && recipe.parameterExpression is null)
            throw new ArgumentException("Explicit recipe ABI inputs require a fingerprinted native argument expression.", nameof(recipe));
        if (!recipe.abiParameters.IsEmpty && recipe.managedProjection?.managedToNativeExpression is not null)
            throw new NotSupportedException("Multiple native ABI inputs need a managed projection plan that defines their buffer and lifetime protocol; a single-value input expression cannot establish it.");
        if (recipe.parameterExpression is not null) Placeholders(recipe.parameterExpression, recipe.abiParameters.IsEmpty ? ["value", "nativeType"] : names.Append("nativeType"));
        if (recipe.resultExpression is not null) Placeholders(recipe.resultExpression, ["value", "resultType"]);
        return new RecipeLowering(recipe);
    }

    /// <summary>Creates an exact content-identified native expression with explicit user assertion evidence.</summary>
    /// <param name="origin">The responsible recipe or plugin identity.</param>
    /// <param name="text">The complete native expression.</param>
    /// <returns>The immutable fragment preserving the exact input bytes.</returns>
    public static ControlledSourceFragment Expression(
        string origin,
        string text
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new(origin, SourceFragmentKind.Expression, text, new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))))
        { evidence = new(EvidenceLevel.UserAsserted, "recipe:" + origin) };
    }

    /// <summary>Resolves explicit native-size aliases into fixed C carriers using the actual parsed pointer width.</summary>
    /// <param name="recipe">The immutable declarative carrier request.</param>
    /// <param name="pointerSize">The actual source target's parsed pointer width, restricted to supported 4-byte and 8-byte targets.</param>
    /// <returns>The primary C shape and its complete scalar/pointer graph.</returns>
    public (StableId type, ImmutableArray<BridgeType> types) GetCarrier(
        TypeLoweringRecipe recipe,
        int pointerSize
    ) {
        ArgumentNullException.ThrowIfNull(recipe);
        var carrier = Carrier(recipe.cAbiType, pointerSize);
        return (carrier.Type, carrier.Types);
    }

    internal static bool Matches(
        string value,
        string pattern
    ) {
        string normalized = string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
        string glob = string.Concat(pattern.Where(character => !char.IsWhiteSpace(character)));
        return Regex.IsMatch(normalized, "^" + Regex.Escape(glob).Replace("\\*", ".*", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static (StableId Type, ImmutableArray<BridgeType> Types) Carrier(
        string spelling,
        int pointerSize
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(spelling);
        string normalized = spelling.Trim();
        bool pointer = normalized.EndsWith('*');
        string scalar = pointer ? normalized[..^1].Trim() : normalized;
        bool isConst = scalar.StartsWith("const ", StringComparison.Ordinal);
        if (isConst) scalar = scalar[6..].Trim();
        if (scalar is "size_t" or "uintptr_t" or "ptrdiff_t" or "intptr_t")
        {
            if (pointerSize is not (4 or 8)) throw new NotSupportedException("A native-size recipe carrier requires a parsed and supported 4-byte or 8-byte pointer ABI.");
            scalar = (scalar is "size_t" or "uintptr_t" ? "uint" : "int") + (pointerSize * 8).ToString(System.Globalization.CultureInfo.InvariantCulture) + "_t";
        }
        if (scalar is not ("int8_t" or "uint8_t" or "int16_t" or "uint16_t" or "int32_t" or "uint32_t" or "int64_t" or "uint64_t" or "float" or "double" or "void")
            || !pointer && (isConst || scalar == "void"))
            throw new NotSupportedException("A declarative native recipe carrier must be a supported fixed-width scalar or one pointer to that scalar.");
        BridgeType storage = new(new("bridge-scalar:" + scalar), BridgeTypeKind.Scalar, scalar);
        if (!pointer) return (storage.id, [storage]);
        BridgeType address = new(new("bridge-pointer:" + (isConst ? "const:" : string.Empty) + storage.id.value), BridgeTypeKind.Pointer, string.Empty)
        { elementType = storage.id, isConst = isConst };
        return (address.id, [storage, address]);
    }

    private static void Placeholders(
        string expression,
        IEnumerable<string> names
    ) {
        if (expression.Length > 1024 * 1024 || expression.Contains('\0')) throw new ArgumentException("A recipe native expression exceeds its limit or contains a null character.");
        HashSet<string> allowed = names.ToHashSet(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(expression, "\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.CultureInvariant))
            if (!allowed.Contains(match.Groups[1].Value)) throw new ArgumentException("A recipe native expression uses an undeclared input placeholder.");
    }

    internal static void ValidateRelease(CppResultReleaseRequest release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(release.nativeFunction);
        ArgumentException.ThrowIfNullOrWhiteSpace(release.allocatorDomain);
        ArgumentNullException.ThrowIfNull(release.evidence);
        if (!Regex.IsMatch(release.nativeFunction, "^(::)?[A-Za-z][A-Za-z0-9_]*(::[A-Za-z][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)
            || release.releaseThreadDomain is not ("any" or "unknown") || release.evidence.level is not (EvidenceLevel.UserAsserted or EvidenceLevel.Declared))
            throw new ArgumentException("Owned native recipes require an exact release identity and explicit non-Verified ownership/thread-domain evidence.", nameof(release));
    }

    private sealed class RecipeLowering(TypeLoweringRecipe recipe) : INativeTypeLowering
    {
        public bool CanLower(
            NativeTypeView type,
            LoweringContext context
        ) => Matches(context.module.GetType(type.id).spelling, recipe.typePattern);
        public NativeTypeLoweringPlan Lower(
            NativeTypeView type,
            LoweringContext context,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            var carrier = Carrier(recipe.cAbiType, context.module.pointerSize);
            bool pointer = carrier.Types.Single(item => item.id == carrier.Type).kind == BridgeTypeKind.Pointer;
            Dictionary<StableId, BridgeType> graph = carrier.Types.ToDictionary(type => type.id);
            ImmutableArray<CppAbiInput>.Builder bindings = ImmutableArray.CreateBuilder<CppAbiInput>();
            foreach (RecipeAbiParameter parameter in recipe.abiParameters)
            {
                var input = Carrier(parameter.cAbiType, context.module.pointerSize);
                foreach (BridgeType shape in input.Types) graph[shape.id] = shape;
                bindings.Add(new(parameter.name, input.Type));
            }
            return new(carrier.Type, graph.Values.OrderBy(shape => shape.id.value, StringComparer.Ordinal).ToImmutableArray(),
                recipe.parameterExpression is not null ? BridgeConversionKind.SourceExpression : pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast,
                recipe.resultExpression is not null ? BridgeConversionKind.SourceExpression : pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast)
            {
                parameterExpression = recipe.parameterExpression is null ? null : Expression(recipe.id, recipe.parameterExpression),
                parameterBindings = bindings.ToImmutable(),
                resultExpression = recipe.resultExpression is null ? null : Expression(recipe.id, recipe.resultExpression),
                sourceFragments = recipe.sourceFragments,
                managedProjection = recipe.managedProjection,
                ownedResultRelease = recipe.ownedResultRelease
            };
        }
    }
}
