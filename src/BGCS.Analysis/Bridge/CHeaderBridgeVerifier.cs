using System;
using System.Linq;
using BGCS.Analysis.Types;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Independently compares emitted C helper prototypes with their original adjusted native signatures.</summary>
public sealed class CHeaderBridgeVerifier
{
    /// <summary>Rejects missing, changed or incomplete helper declarations after an ordinary frontend reparse.</summary>
    /// <param name="original">The original C source graph.</param>
    /// <param name="plan">The checked real export mappings.</param>
    /// <param name="reparsed">The generated header parsed with the original target and toolchain.</param>
    public void Verify(
        NativeModule original,
        CHeaderBridgeModule plan,
        NativeModule reparsed
    ) {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reparsed);
        if (original.targetId != reparsed.targetId || original.triple != reparsed.triple || original.pointerSize != reparsed.pointerSize || original.byteOrder != reparsed.byteOrder)
            throw new InvalidOperationException("A C header adapter was reparsed for a different target ABI.");
        NativeTypeFormatter formatter = new();
        foreach (CHeaderBridgeExport export in plan.exports)
        {
            NativeDeclaration[] matches = reparsed.declarations.Where(declaration => declaration.kind == "Function" && declaration.name == export.exportName).ToArray();
            if (matches.Length != 1 || matches[0].isStatic || matches[0].isVariadic || !matches[0].hasPrototype
                || matches[0].callingConvention is not ("C" or "Default") || matches[0].returnType is null)
                throw new InvalidOperationException($"C header adapter export '{export.exportName}' does not have its required exact external C prototype.");
            NativeDeclaration actual = matches[0];
            var parameters = actual.children.Select(reparsed.GetDeclaration).Where(declaration => declaration.kind == "Parameter").ToArray();
            if (parameters.Length != export.parameters.Length || !Same(export.returnType, actual.returnType!.Value)
                || parameters.Where((
                    parameter,
                    index
                ) => parameter.type is null || !Same(export.parameters[index].type, parameter.type.Value)).Any())
                throw new InvalidOperationException($"C header adapter export '{export.exportName}' changed its original native parameter or return contract.");
        }
        bool Same(
            BGCS.Contracts.Common.StableId expected,
            BGCS.Contracts.Common.StableId actual
        )
            => formatter.Format(original, expected, preserveAliases: false) == formatter.Format(reparsed, actual, preserveAliases: false)
                && original.GetType(expected).size == reparsed.GetType(actual).size && original.GetType(expected).alignment == reparsed.GetType(actual).alignment;
    }
}
