using System;
using System.Collections.Generic;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Inputs;
using BGCS.Analysis.Types;

namespace BGCS.NativeBridge.Planning;

/// <summary>Reconstructs source types outside their declaration scope without dropping nested qualifiers.</summary>
internal static class NativeTypeSpelling
{
    internal static string Format(
        NativeModule module,
        StableId id
    ) {
        HashSet<StableId> visited = [];
        for (NativeType shape = module.GetType(id); visited.Add(shape.id);)
        {
            if (shape.category == NativeTypeCategory.Function)
                break;
            if (shape.category == NativeTypeCategory.Array)
                throw new NotSupportedException("An array type needs an explicit native declarator lowering.");
            if (shape.category is not (NativeTypeCategory.Pointer or NativeTypeCategory.Reference or NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                || shape.elementType is not StableId element) break;
            shape = module.GetType(element);
        }
        NativeTypeFormatter formatter = new();
        return module.language == SourceLanguage.C
            ? formatter.FormatDeclaration(module, id, string.Empty, preserveAliases: false)
            : formatter.Format(module, id, preserveAliases: false, absoluteNames: true);
    }
}
