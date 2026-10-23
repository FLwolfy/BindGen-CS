using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Analysis.Bridge;

namespace BGCS.Analysis.Interop;

/// <summary>Prevents semantic extensions from silently rewriting an already established native calling contract.</summary>
public sealed class InteropModuleVerifier
{
    /// <summary>Allows semantic enrichment and export selection while requiring ABI changes to use a native bridge validation boundary.</summary>
    /// <param name="baseline">The established native ABI.</param>
    /// <param name="candidate">The transformed semantic contract.</param>
    /// <exception cref="InvalidOperationException">An extension changed or invented an unverified native ABI shape.</exception>
    public void VerifyPreservedAbi(
        InteropModule baseline,
        InteropModule candidate
    ) {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        new VirtualProxyProtocolVerifier().Verify(baseline);
        new VirtualProxyProtocolVerifier().Verify(candidate);
        new NativeFunctionAliasVerifier().Verify(baseline);
        new NativeFunctionAliasVerifier().Verify(candidate);
        if (candidate.libraryId != baseline.libraryId || candidate.targetId != baseline.targetId || candidate.pointerSize != baseline.pointerSize || candidate.byteOrder != baseline.byteOrder)
            throw new InvalidOperationException("An interoperability transformation changed the resolved library or target identity.");
        Dictionary<StableId, InteropType> types = baseline.types.ToDictionary(type => type.id);
        if (types.Count != candidate.types.Length || candidate.types.Any(type => !types.TryGetValue(type.id, out InteropType? established) || type != established))
            throw new InvalidOperationException("Native storage changes require an independently validated bridge or native-fact boundary.");
        Dictionary<StableId, InteropFunction> functions = baseline.functions.ToDictionary(function => function.id);
        HashSet<StableId> exports = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (InteropFunction function in candidate.functions)
        {
            if (!exports.Add(function.id) || !names.Add(function.exportName) || !functions.TryGetValue(function.id, out InteropFunction? established))
                throw new InvalidOperationException("An interoperability transformation added or duplicated an unverified native export.");
            if (function.exportName != established.exportName || function.returnType != established.returnType || function.callingConvention != established.callingConvention
                || function.isVariadic != established.isVariadic || function.hasPrototype != established.hasPrototype || function.parameters.Length != established.parameters.Length)
                throw new InvalidOperationException("Native callable changes require an independently validated bridge boundary.");
            for (int index = 0; index < function.parameters.Length; index++)
                if (function.parameters[index].id != established.parameters[index].id || function.parameters[index].type != established.parameters[index].type)
                    throw new InvalidOperationException("A semantic extension changed the ordered native parameter ABI.");
        }
        Dictionary<StableId, VirtualProxyContract> protocols = baseline.virtualProxies.ToDictionary(proxy => proxy.id);
        foreach (VirtualProxyContract proxy in candidate.virtualProxies)
            if (!protocols.TryGetValue(proxy.id, out VirtualProxyContract? established)
                || proxy with { lifetime = established.lifetime, quiescence = established.quiescence } != established)
                throw new InvalidOperationException("A semantic transformation changed an independently checked native virtual proxy ABI protocol.");
        Dictionary<StableId, InteropFunctionAlias> aliases = baseline.functionAliases.ToDictionary(alias => alias.sourceDeclaration);
        foreach (InteropFunctionAlias alias in candidate.functionAliases)
            if (!aliases.TryGetValue(alias.sourceDeclaration, out InteropFunctionAlias? established) || alias != established)
                throw new InvalidOperationException("A semantic transformation changed an independently established native macro alias target.");
        VerifyConstants(baseline, candidate);
    }

    private static void VerifyConstants(
        InteropModule baseline,
        InteropModule candidate
    ) {
        if (baseline.constants.IsDefault || baseline.stringConstants.IsDefault || candidate.constants.IsDefault || candidate.stringConstants.IsDefault)
            throw new InvalidOperationException("Native constant collections must remain initialized at a semantic transformation boundary.");
        Dictionary<StableId, InteropConstant> scalars = baseline.constants.ToDictionary(constant => constant.sourceDeclaration);
        Dictionary<StableId, InteropStringConstant> strings = baseline.stringConstants.ToDictionary(constant => constant.sourceDeclaration);
        if (scalars.Keys.Any(strings.ContainsKey)) throw new InvalidOperationException("A native constant cannot carry both scalar and string facts.");
        HashSet<StableId> selected = [];
        foreach (InteropConstant constant in candidate.constants)
            if (!selected.Add(constant.sourceDeclaration) || !scalars.TryGetValue(constant.sourceDeclaration, out InteropConstant? established)
                || constant.name != established.name || constant.qualifiedName != established.qualifiedName || constant.value != established.value)
                throw new InvalidOperationException("A semantic transformation changed or invented an established native scalar constant.");
        foreach (InteropStringConstant constant in candidate.stringConstants)
        {
            if (!selected.Add(constant.sourceDeclaration) || !strings.TryGetValue(constant.sourceDeclaration, out InteropStringConstant? established)
                || constant.name != established.name || constant.qualifiedName != established.qualifiedName || constant.value is null || established.value is null
                || constant.value.kind != established.value.kind || constant.value.unitWidth != established.value.unitWidth
                || constant.value.evidence != established.value.evidence || constant.value.codeUnits.IsDefault || constant.value.literalTokens.IsDefault
                || !constant.value.codeUnits.SequenceEqual(established.value.codeUnits) || !constant.value.literalTokens.SequenceEqual(established.value.literalTokens))
                throw new InvalidOperationException("A semantic transformation changed or invented established native string code units or provenance.");
            new NativeStringConstantVerifier().Verify(constant.value);
        }
    }
}
