using System;
using System.Collections.Generic;
using BGCS.Analysis.Safety;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

/// <summary>Plans explicit one-value pointer conversions without changing the raw import signature.</summary>
public sealed class ValueParameterPlanner
{
    /// <summary>Checks the generated pointee carrier and plans an address-checked borrow of original managed storage.</summary>
    /// <param name="module">The native ABI.</param>
    /// <param name="function">The containing function.</param>
    /// <param name="parameter">The exact annotated pointer.</param>
    /// <param name="resolve">The backend's checked raw type resolver.</param>
    /// <param name="storage">Already verified generated raw record storage keyed by native type.</param>
    /// <returns>The explicit direction, pointee carrier and required address validation.</returns>
    public CSharpValueParameterPlan Plan(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter,
        Func<StableId, string> resolve,
        IReadOnlyDictionary<StableId, CSharpStoragePlan> storage
    ) {
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(storage);
        ValueParameterAnalyzer analyzer = new();
        if (analyzer.GetRestriction(module, function, parameter) is string restriction) throw new NotSupportedException(restriction);
        InteropType pointer = Underlying(parameter.type), element = Underlying(pointer.elementType!.Value);
        int alignment = analyzer.GetAlignment(module, parameter);
        if (alignment > 8) throw new NotSupportedException("Over-aligned typed values require explicitly aligned native storage rather than an unproven managed reference alignment.");
        if (element.category == NativeTypeCategory.Record && (!storage.TryGetValue(element.id, out CSharpStoragePlan? carrier)
            || carrier.size != element.size || carrier.managedAlignment != element.alignment || carrier.managedAlignment is null
            || !carrier.bitfields.IsEmpty || !carrier.flexibleArrays.IsEmpty || carrier.arrayLength is not null))
            throw new NotSupportedException("A typed record value requires independently verified generated raw storage and managed alignment.");
        int parameterIndex = -1;
        for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == parameter.id) parameterIndex = index;
        if (parameterIndex < 0) throw new InvalidOperationException("A typed value parameter does not belong to the declared native function.");
        string carrierType = resolve(element.id);
        bool boolean = element.category == NativeTypeCategory.Primitive && element.primitiveKind == "Bool";
        if (boolean && carrierType != "byte") throw new NotSupportedException("A Boolean value borrow requires the checked raw one-byte Boolean carrier.");
        return new(parameterIndex, carrierType, parameter.singleValue!.direction, alignment, CSharpValueAllocationMode.AddressCheckedPinnedBorrow)
        { managedElementType = boolean ? "bool" : carrierType };

        InteropType Underlying(StableId identity)
        {
            InteropType type = module.GetType(identity);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
            return type;
        }
    }
}

/// <summary>Identifies the implemented synchronous address-borrowing strategy.</summary>
public enum CSharpValueAllocationMode
{
    /// <summary>The original managed storage stays pinned and its actual address alignment is checked before the native call.</summary>
    AddressCheckedPinnedBorrow
}

/// <summary>Plans an explicit in, ref or out borrow of checked raw storage without destroying native-visible aliases.</summary>
/// <param name="parameterIndex">The native pointer's index.</param>
/// <param name="elementType">The checked raw pointee syntax.</param>
/// <param name="direction">The declared native read and write access to borrowed storage.</param>
/// <param name="alignment">The actual address alignment required before passing the local.</param>
/// <param name="allocationMode">The explicit storage-borrow and address-validation strategy.</param>
public sealed record CSharpValueParameterPlan(
    int parameterIndex,
    string elementType,
    NativeParameterDirection direction,
    int alignment,
    CSharpValueAllocationMode allocationMode
)
{
    /// <summary>Gets the original managed storage type borrowed by the facade; the exact raw ABI element remains separate.</summary>
    public string managedElementType { get; init; } = elementType;
}
