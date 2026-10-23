using System;
using System.Collections.Generic;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Safety;

/// <summary>Matches explicit native receiver identities without treating unrelated opaque aliases as interchangeable.</summary>
public static class NativeReceiverTypeMatcher
{
    /// <summary>Matches a record pointer or the exact named opaque-pointer alias selected by presentation.</summary>
    /// <param name="module">The frozen native ABI.</param>
    /// <param name="selectedType">The exact selected record or named pointer alias.</param>
    /// <param name="valueType">The actual return or receiver parameter type.</param>
    /// <returns>Whether the exact receiver identity is preserved.</returns>
    public static bool Matches(
        InteropModule module,
        StableId selectedType,
        StableId valueType
    ) {
        InteropType selected = module.GetType(selectedType), canonical = ValueParameterAnalyzer.Resolve(module, selectedType);
        if (canonical.category == NativeTypeCategory.Record)
        {
            InteropType pointer = ValueParameterAnalyzer.Resolve(module, valueType);
            return pointer.category == NativeTypeCategory.Pointer && pointer.elementType is StableId element && ValueParameterAnalyzer.Resolve(module, element).id == canonical.id;
        }
        if (selected.category != NativeTypeCategory.Alias || canonical.category != NativeTypeCategory.Pointer) return false;
        HashSet<StableId> active = [];
        InteropType view = module.GetType(valueType);
        while (true)
        {
            if (!active.Add(view.id)) throw new InvalidOperationException("A receiver alias graph is cyclic.");
            if (view.id == selectedType) return true;
            if (view.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified) || view.elementType is not StableId element) return false;
            view = module.GetType(element);
        }
    }

    /// <summary>Checks a borrowed pointer conversion without removing native pointee qualifications.</summary>
    /// <param name="module">The frozen native type graph.</param>
    /// <param name="ownedPointer">The actual factory pointer type.</param>
    /// <param name="receiverPointer">The actual member receiver pointer type.</param>
    /// <returns>Whether the pointer keeps every established const/volatile qualification and exact underlying identity.</returns>
    public static bool CanBorrowWithoutRemovingQualifiers(
        InteropModule module,
        StableId ownedPointer,
        StableId receiverPointer
    ) {
        InteropType source = ValueParameterAnalyzer.Resolve(module, ownedPointer), destination = ValueParameterAnalyzer.Resolve(module, receiverPointer);
        if (source.category != NativeTypeCategory.Pointer || destination.category != NativeTypeCategory.Pointer
            || source.elementType is not StableId sourceElement || destination.elementType is not StableId destinationElement) return false;
        if (!Pointee(sourceElement, out InteropType from, out HashSet<string> sourceQualifiers)
            || !Pointee(destinationElement, out InteropType to, out HashSet<string> destinationQualifiers)) return false;
        // No recursive pointer covariance: the underlying pointee identity must be identical.
        return from.id == to.id && sourceQualifiers.IsSubsetOf(destinationQualifiers);

        bool Pointee(
            StableId identity,
            out InteropType type,
            out HashSet<string> qualifiers
        ) {
            type = module.GetType(identity);
            qualifiers = new(StringComparer.Ordinal);
            HashSet<StableId> active = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!active.Add(type.id)) throw new InvalidOperationException("A borrowed receiver qualifier graph is cyclic.");
                if (type.category == NativeTypeCategory.Qualified)
                {
                    if (type.qualifier is not ("Const" or "Volatile")) return false;
                    qualifiers.Add(type.qualifier);
                }
                if (type.elementType is not StableId element) return false;
                type = module.GetType(element);
            }
            return true;
        }
    }
}
