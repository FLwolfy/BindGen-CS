using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Interop;

/// <summary>Projects selected source constants without changing the final C calling contract.</summary>
public sealed class NativeConstantAnalyzer
{
    /// <summary>Transfers compiler-established scalar values and reports unsupported object-like replacements honestly.</summary>
    /// <param name="source">The selected detached source declarations.</param>
    /// <param name="interop">The independently verified final callable ABI.</param>
    /// <param name="publicDeclarations">Exact public root identities, or null for caller-managed source selection.</param>
    /// <returns>The ABI enriched with immutable native constants and omission diagnostics.</returns>
    public InteropModule Apply(
        NativeModule source,
        InteropModule interop,
        IEnumerable<StableId>? publicDeclarations = null
    ) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(interop);
        if (source.targetId != interop.targetId || source.pointerSize != interop.pointerSize || source.byteOrder != interop.byteOrder)
            throw new ArgumentException("Native constants must originate from the same resolved target as the callable ABI.");
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = interop.diagnostics.ToBuilder();
        ImmutableArray<InteropConstant>.Builder constants = ImmutableArray.CreateBuilder<InteropConstant>();
        ImmutableArray<InteropStringConstant>.Builder strings = ImmutableArray.CreateBuilder<InteropStringConstant>();
        HashSet<StableId>? selected = publicDeclarations?.ToHashSet();
        foreach (NativeDeclaration declaration in source.declarations)
        {
            if (selected is not null && !selected.Contains(declaration.id) || declaration.visibility is "Private" or "Protected") continue;
            if (declaration.stringConstant is { } text && (declaration.kind == "Macro" && declaration.macro?.isActive != false
                || declaration.kind == "Field" && declaration.role == "Variable"))
                strings.Add(new(declaration.id, declaration.name, declaration.qualifiedName, text, declaration.documentation));
            else if (declaration.constantValue is { } value && (declaration.kind == "Macro" && declaration.macro?.isActive != false
                || declaration.kind == "Field" && declaration.role == "Variable"))
                constants.Add(new(declaration.id, declaration.name, declaration.qualifiedName, value, declaration.documentation));
            else if (declaration.kind == "Macro" && declaration.macro is { isFunctionLike: false, isActive: true, functionAlias: null }
                && !string.IsNullOrWhiteSpace(declaration.value))
                diagnostics.Add(new(BindingDiagnosticSeverity.Information,
                    $"Object macro '{declaration.qualifiedName}' has no established supported scalar constant. Its original replacement remains available in native facts for an explicit transformation.",
                    "BGCS-CONSTANT-OMITTED")
                { location = declaration.source });
        }
        InteropModule result = new(interop.libraryId, interop.targetId, interop.pointerSize, interop.types, interop.functions, diagnostics.ToImmutable())
        {
            byteOrder = interop.byteOrder,
            constants = constants.OrderBy(constant => constant.sourceDeclaration.value, StringComparer.Ordinal).ToImmutableArray(),
            virtualProxies = interop.virtualProxies,
            functionAliases = interop.functionAliases,
            stringConstants = strings.OrderBy(constant => constant.sourceDeclaration.value, StringComparer.Ordinal).ToImmutableArray()
        };
        return new NativeFunctionAliasAnalyzer().Apply(source, result, publicDeclarations);
    }
}
