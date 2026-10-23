using System;
using System.Collections.Generic;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Analysis.Layout;

/// <summary>Separates native storage facts from special scalar and alignment calling restrictions.</summary>
public sealed class ByValueCompatibilityAnalyzer
{
    /// <summary>Finds native restrictions that require a pointer or a verified bridge carrier.</summary>
    /// <param name="view">The native type use.</param>
    /// <returns>A restriction description, or null when no native-only restriction is found.</returns>
    /// <remarks>A null result still requires the selected backend to prove the managed carrier and calling ABI.</remarks>
    public string? GetNativeRestriction(NativeTypeView view)
    {
        return Inspect(view, []);
    }

    private static string? Inspect(
        NativeTypeView view,
        HashSet<StableId> active
    ) {
        if (!active.Add(view.id))
            return "The native by-value type graph contains a cycle.";
        try
        {
            NativeType type = view.type;
            if (type.category == NativeTypeCategory.Block)
                return "Objective-C blocks require their own invocation and lifetime bridge; they are not ordinary C function pointers.";
            if (type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Reference)
                return null;
            if (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Array)
                return view.element is NativeTypeView element ? Inspect(element, active) : "The native element type is unknown.";
            if (type.primitiveKind == "LongDouble" && type.size > 8)
                return "Native long double requires verified calling-ABI evidence or an explicit conversion shim.";
            if (type.primitiveKind is "Float16" or "BFloat16" or "Half")
                return "Native half-precision scalars require verified calling-ABI evidence or an explicit conversion shim.";
            if (type.category == NativeTypeCategory.Primitive && type.size > 8)
                return "A special native scalar requires verified calling-ABI evidence or an explicit conversion shim.";
            if (type.category == NativeTypeCategory.Record && type.alignment > 16)
                return "Over-aligned records require verified allocation and calling-ABI evidence or a pointer bridge.";
            if (type.category == NativeTypeCategory.Record)
            {
                if (type.size is null || type.alignment is null || type.declaration is not StableId declarationId)
                    return "The native record's complete storage and field shape is unknown.";
                NativeDeclaration declaration = view.module.GetDeclaration(declarationId);
                foreach (StableId childId in declaration.children)
                {
                    NativeDeclaration child = view.module.GetDeclaration(childId);
                    if (child.kind != "Field" || child.isStatic || child.type is not StableId fieldType)
                        continue;
                    string? restriction = Inspect(new(view.module, fieldType), active);
                    if (restriction is not null)
                        return $"Native field '{child.qualifiedName}': {restriction}";
                }
            }
            if (type.category == NativeTypeCategory.Unexposed)
                return "The native type shape is not available for by-value ABI classification.";
            return null;
        }
        finally
        {
            active.Remove(view.id);
        }
    }
}
