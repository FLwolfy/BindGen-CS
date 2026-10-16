using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;

namespace BGCS.Analysis.Interop;

/// <summary>Checks source aliases against existing primary exports without allowing them to invent native ABI.</summary>
public sealed class NativeFunctionAliasVerifier
{
    /// <summary>Rejects dangling, ambiguous, duplicated or unsupported native alias claims.</summary>
    /// <param name="module">The immutable interoperability module.</param>
    public void Verify(InteropModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.functionAliases.IsDefault) throw new InvalidOperationException("Native function aliases require an initialized immutable collection.");
        var functions = module.functions.ToDictionary(function => function.id);
        HashSet<StableId> identities = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (InteropFunctionAlias alias in module.functionAliases)
            if (alias is null || !identities.Add(alias.sourceDeclaration) || string.IsNullOrWhiteSpace(alias.name)
                || string.IsNullOrWhiteSpace(alias.qualifiedName) || !names.Add(alias.qualifiedName)
                || alias.evidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified }
                || string.IsNullOrWhiteSpace(alias.evidence.origin)
                || !functions.TryGetValue(alias.nativeFunction, out InteropFunction? function) || function.inputAdapterOf is not null)
                throw new InvalidOperationException("A native macro alias must identify one existing primary export and preserve its compiler provenance.");
    }
}
