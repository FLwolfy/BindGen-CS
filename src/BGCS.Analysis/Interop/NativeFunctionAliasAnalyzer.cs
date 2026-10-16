using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Interop;

/// <summary>Associates compiler-established macro aliases with an existing final primary native export.</summary>
public sealed class NativeFunctionAliasAnalyzer
{
    /// <summary>Preserves exact source aliases without copying or inventing native callable ABI.</summary>
    /// <param name="source">The selected original facts.</param>
    /// <param name="module">The final independently checked native exports.</param>
    /// <param name="publicDeclarations">The selected public roots, or null for caller-owned selection.</param>
    /// <returns>The unchanged native ABI with exact alias metadata and actionable unresolved-alias errors.</returns>
    public InteropModule Apply(
        NativeModule source,
        InteropModule module,
        IEnumerable<StableId>? publicDeclarations = null
    ) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(module);
        if (source.targetId != module.targetId || source.pointerSize != module.pointerSize || source.byteOrder != module.byteOrder)
            throw new ArgumentException("Native aliases must originate from the final ABI's exact target.");
        HashSet<StableId>? selected = publicDeclarations?.ToHashSet();
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = module.diagnostics.ToBuilder();
        ImmutableArray<InteropFunctionAlias>.Builder aliases = ImmutableArray.CreateBuilder<InteropFunctionAlias>();
        foreach (NativeDeclaration declaration in source.declarations.Where(declaration => declaration.kind == "Macro"
            && declaration.macro is { isActive: true, isFunctionLike: false, functionAlias: not null }))
        {
            if (selected is not null && !selected.Contains(declaration.id)) continue;
            NativeMacroDefinition macro = declaration.macro!;
            if (macro.functionAliasEvidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified })
                throw new InvalidOperationException("A native macro alias omitted its compiler provenance.");
            InteropFunction[] matches = module.functions.Where(function => function.sourceDeclaration == macro.functionAlias && function.inputAdapterOf is null).ToArray();
            if (matches.Length != 1)
            {
                diagnostics.Add(new(BindingDiagnosticSeverity.Error,
                    $"Macro alias '{declaration.qualifiedName}' has no unique checked primary export for its exact source function. Preserve that callable in source selection and lowering, or provide an explicit unambiguous projection.",
                    "BGCS-MACRO-ALIAS")
                { location = declaration.source });
                continue;
            }
            aliases.Add(new(declaration.id, declaration.name, declaration.qualifiedName, matches[0].id, macro.functionAliasEvidence)
            { source = declaration.source, documentation = declaration.documentation, documentationContent = declaration.documentationContent });
        }
        InteropModule result = new(module.libraryId, module.targetId, module.pointerSize, module.types, module.functions, diagnostics.ToImmutable())
        {
            byteOrder = module.byteOrder,
            constants = module.constants,
            virtualProxies = module.virtualProxies,
            stringConstants = module.stringConstants,
            functionAliases = aliases.OrderBy(alias => alias.sourceDeclaration.value, StringComparer.Ordinal).ToImmutableArray()
        };
        new NativeFunctionAliasVerifier().Verify(result);
        return result;
    }
}
