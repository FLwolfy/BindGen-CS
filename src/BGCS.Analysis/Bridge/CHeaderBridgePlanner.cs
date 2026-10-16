using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Allocates real C exports for selected internal or inline definitions without assuming their original names exist in a shared library.</summary>
public sealed class CHeaderBridgePlanner
{
    /// <summary>Plans exact C wrapper signatures from a detached source graph.</summary>
    /// <param name="module">The original C facts.</param>
    /// <param name="selectedCallables">The explicitly selected public callable identities.</param>
    /// <param name="exportPrefix">The unreserved native export prefix.</param>
    /// <returns>The immutable helper mappings requiring compilation into the configured native module.</returns>
    public CHeaderBridgeModule Plan(
        NativeModule module,
        IEnumerable<StableId> selectedCallables,
        string exportPrefix
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(selectedCallables);
        if (module.language != SourceLanguage.C) throw new ArgumentException("C header adapters require C source facts.", nameof(module));
        if (!Regex.IsMatch(exportPrefix, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || exportPrefix.Contains("__", StringComparison.Ordinal))
            throw new ArgumentException("A C header adapter prefix must be an unreserved C identifier.", nameof(exportPrefix));
        ImmutableArray<CHeaderBridgeExport>.Builder exports = ImmutableArray.CreateBuilder<CHeaderBridgeExport>();
        HashSet<string> names = module.declarations.Where(declaration => declaration.kind == "Function").Select(declaration => declaration.name).ToHashSet(StringComparer.Ordinal);
        foreach (StableId id in selectedCallables.Distinct().OrderBy(id => id.value, StringComparer.Ordinal))
        {
            NativeDeclaration callable = module.GetDeclaration(id);
            if (!CHeaderFunctionPolicy.RequiresAdapter(callable)) continue;
            if ((!callable.isDefinition && callable.addressability != NativeCallableAddressability.DirectCallOnly) || !callable.hasPrototype || callable.isVariadic || callable.returnType is null)
                throw new NotSupportedException($"C header helper '{callable.qualifiedName}' requires an accessible definition and a fixed native prototype before its adapter can be generated.");
            if (callable.addressability == NativeCallableAddressability.DirectCallOnly)
            {
                NativeType result = module.GetType(callable.returnType.Value);
                while (result.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) result = module.GetType(result.elementType!.Value);
                if (callable.addressabilityEvidence?.level != BGCS.Contracts.Common.EvidenceLevel.Declared || result.primitiveKind != "Void"
                    || callable.children.Select(module.GetDeclaration).Any(child => child.kind == "Parameter"))
                    throw new NotSupportedException($"Direct-call-only compiler intrinsic '{callable.qualifiedName}' requires exact native provider proof; only a declared fixed void(void) shape is currently supported.");
            }
            if (!Regex.IsMatch(callable.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                throw new NotSupportedException("A C header helper needs an exact C source identifier.");
            string name = exportPrefix.TrimEnd('_') + "_helper_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.value)))[..20];
            if (!names.Add(name)) throw new InvalidOperationException("A C header adapter export collides with another native callable.");
            var parameters = callable.children.Select(module.GetDeclaration).Where(declaration => declaration.kind == "Parameter")
                .Select(parameter => new CHeaderBridgeParameter(parameter.id, parameter.type ?? throw new InvalidOperationException("A C helper parameter omitted its adjusted ABI type."))).ToImmutableArray();
            foreach (StableId type in parameters.Select(parameter => parameter.type).Append(callable.returnType.Value)) Check(type, []);
            exports.Add(new(id, callable.name, name, callable.returnType.Value, parameters));
        }
        return new(exportPrefix, exports.ToImmutable());

        void Check(
            StableId id,
            HashSet<StableId> active
        ) {
            if (!active.Add(id)) return;
            NativeType type = module.GetType(id);
            if (type.category is NativeTypeCategory.Function or NativeTypeCategory.Block)
            {
                if (type.category == NativeTypeCategory.Block || !type.hasPrototype || type.callingConvention is not (null or "C" or "Default"))
                    throw new NotSupportedException("A C helper function-pointer type requires a supported exact native declaration or an explicit custom shim.");
                if (type.returnType is StableId result) Check(result, active);
                foreach (StableId parameter in type.parameterTypes) Check(parameter, active);
            }
            if (type.elementType is StableId element) Check(element, active);
        }
    }
}
