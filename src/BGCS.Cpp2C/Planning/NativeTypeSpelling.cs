using System;
using System.Collections.Generic;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Cpp2C.Planning;

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
                return new BGCS.Analysis.Types.NativeTypeFormatter().Format(module, id, preserveAliases: false, absoluteNames: true);
            if (shape.category is not (NativeTypeCategory.Pointer or NativeTypeCategory.Reference or NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                || shape.elementType is not StableId element) break;
            shape = module.GetType(element);
        }
        NativeType type = module.GetType(id);
        if (type.category == NativeTypeCategory.Alias) return Format(module, type.elementType!.Value);
        if (type.category == NativeTypeCategory.Qualified)
        {
            string qualifiers = (type.qualifier?.Contains("Const", StringComparison.Ordinal) == true ? " const" : string.Empty)
                + (type.qualifier?.Contains("Volatile", StringComparison.Ordinal) == true ? " volatile" : string.Empty);
            return Format(module, type.elementType!.Value) + qualifiers;
        }
        if (type.category == NativeTypeCategory.Pointer) return Format(module, type.elementType!.Value) + " *";
        if (type.category == NativeTypeCategory.Reference) return Format(module, type.elementType!.Value) + (type.isRValueReference ? " &&" : " &");
        if (type.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
        {
            string name = type.spelling.Contains('<', StringComparison.Ordinal) ? type.spelling : module.GetDeclaration(type.declaration!.Value).qualifiedName;
            return name.StartsWith("::", StringComparison.Ordinal) ? name : "::" + name;
        }
        if (type.category == NativeTypeCategory.Array)
            throw new NotSupportedException("An array type needs an explicit native declarator lowering.");
        return type.spelling;
    }
}
