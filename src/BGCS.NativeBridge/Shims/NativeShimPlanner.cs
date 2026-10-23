using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Shims;

/// <summary>Normalizes explicitly declared shim inputs and verifies their actual detached C-linkage symbols.</summary>
public sealed class NativeShimPlanner
{
    /// <summary>Creates an immutable input plan without copying user files or discovering tools.</summary>
    /// <param name="request">The public C headers, implementation sources and exact exports.</param>
    /// <param name="pathBase">The absolute base of the declaring configuration document.</param>
    /// <returns>The normalized explicit input plan.</returns>
    public NativeShimPlan Plan(
        NativeShimRequest request,
        string pathBase
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        if (!Path.IsPathFullyQualified(pathBase)) throw new ArgumentException("A native shim requires an absolute configuration path base.", nameof(pathBase));
        if (request.publicHeaders.IsDefaultOrEmpty || request.sourceFiles.IsDefault || request.exports.IsDefaultOrEmpty)
            throw new ArgumentException("A native shim requires initialized file collections, public headers and explicitly declared exports.", nameof(request));
        if (request.evidence.level is not (EvidenceLevel.UserAsserted or EvidenceLevel.Declared))
            throw new ArgumentException("A native shim must retain its explicit semantic assertion and cannot assert independent verification.", nameof(request));
        ImmutableArray<string> headers = Paths(request.publicHeaders, false);
        ImmutableArray<string> sources = Paths(request.sourceFiles, true);
        if (headers.Concat(sources).Distinct(StringComparer.Ordinal).Count() != headers.Length + sources.Length)
            throw new ArgumentException("A native shim input path is declared more than once.", nameof(request));
        foreach (string symbol in request.exports)
            if (!Regex.IsMatch(symbol ?? string.Empty, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                throw new ArgumentException("A shim export must be an exact C symbol identity.", nameof(request));
        if (request.exports.Distinct(StringComparer.Ordinal).Count() != request.exports.Length)
            throw new ArgumentException("A native shim export is declared more than once.", nameof(request));
        return new(request.id, headers, sources, request.exports, request.evidence);

        ImmutableArray<string> Paths(
            ImmutableArray<string> inputs,
            bool implementations
        )
            => inputs.Select(input =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(input);
                string path = Path.GetFullPath(input, pathBase);
                if (implementations && Path.GetExtension(path) is not (".c" or ".cc" or ".cpp" or ".cxx" or ".C"))
                    throw new ArgumentException("A shim implementation must declare a C or C++ source file.", nameof(request));
                return path;
            }).ToImmutableArray();
    }

    /// <summary>Requires each declared shim symbol to be present with a fixed accessible C-linkage prototype.</summary>
    /// <param name="module">The same worker facts used for ordinary C++ bridge planning.</param>
    /// <param name="plan">The frozen exact shim input plan.</param>
    /// <returns>The explicit detached native callable identities selected for bridge generation.</returns>
    public ImmutableArray<StableId> SelectCallables(
        NativeModule module,
        NativeShimPlan plan
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(plan);
        List<StableId> declarations = [];
        foreach (string symbol in plan.exports)
        {
            NativeDeclaration[] matches = module.declarations.Where(declaration => declaration.kind == "Function" && declaration.name == symbol).ToArray();
            if (matches.Length != 1 || matches[0].isDeleted || !matches[0].hasPrototype || matches[0].isVariadic || matches[0].linkage != "C"
                || matches[0].visibility is "Private" or "Protected" || matches[0].templateParameters.Length != 0)
                throw new InvalidOperationException($"Native shim '{plan.id}' has no unique fixed C-linkage prototype for export '{symbol}'.");
            declarations.Add(matches[0].id);
        }
        return declarations.ToImmutableArray();
    }
}
