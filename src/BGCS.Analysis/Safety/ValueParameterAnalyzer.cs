using System;
using System.Collections.Generic;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Safety;

/// <summary>Validates explicit one-value pointers without assuming direction, nullability, ownership or retention.</summary>
public sealed class ValueParameterAnalyzer
{
    /// <summary>Finds a reason to retain only the exact raw pointer signature.</summary>
    /// <param name="module">The complete native ABI.</param>
    /// <param name="function">The containing native function.</param>
    /// <param name="parameter">The explicitly annotated pointer.</param>
    /// <returns>A restriction, or null when a checked borrow of original value storage is semantically eligible.</returns>
    public string? GetRestriction(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) => GetRestrictionCore(module, function, parameter, nullBinding: false);

    /// <summary>Checks an explicit nullable value protocol solely for passing a configured constant null pointer.</summary>
    /// <param name="module">The complete native ABI.</param>
    /// <param name="function">The containing synchronous native function.</param>
    /// <param name="parameter">The explicitly nullable one-value data pointer.</param>
    /// <returns>A restriction, or null when a null-only binding is eligible; this never authorizes a nullable managed reference.</returns>
    public string? GetNullBindingRestriction(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) => GetRestrictionCore(module, function, parameter, nullBinding: true);

    private string? GetRestrictionCore(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter,
        bool nullBinding
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameter);
        ValueParameterContract? value = parameter.singleValue;
        if (value is null) return "No explicit one-value pointer contract is declared.";
        if (!Known(value.evidence) || !Known(value.lifetime.evidence)) return "Typed value pointers require explicit value and lifetime evidence.";
        if (value.direction is not (NativeParameterDirection.Input or NativeParameterDirection.Output or NativeParameterDirection.InputOutput))
            return "A typed value pointer requires explicit access direction.";
        if (value.lifetime.kind != NativeLifetimeKind.Call || value.retainsPointer != false || function.asynchronous is not null)
            return "A temporary value address requires an explicitly nonretaining synchronous call lifetime.";
        if (value.allowsNull != nullBinding) return nullBinding
            ? "A constant NULL binding requires explicit allowsNull:true; it cannot infer native null acceptance."
            : "A required typed value cannot represent a nullable native address.";
        if (parameter.buffer is not null || parameter.text is not null || parameter.callback is not null)
            return "A typed value pointer cannot simultaneously describe text, buffer or callback storage.";
        if (parameter.ownership is { } ownership && (ownership.kind != NativeOwnershipKind.Borrowed || ownership.lifetime.kind != NativeLifetimeKind.Call || ownership.retainsPointer == true
            || !Known(ownership.evidence) || !Known(ownership.lifetime.evidence))) return "A temporary value pointer cannot transfer ownership or outlive its call.";
        InteropType pointer = Resolve(module, parameter.type);
        if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
            return "A typed value parameter requires one known native pointer level.";
        InteropType leaf = Resolve(module, element);
        if (value.copySource is { } copy)
        {
            if (nullBinding || copy.bridgeExport != function.nativeBridgeExport || copy.sourceCallable != function.sourceDeclaration
                || copy.storageType != leaf.id || copy.sourceType != leaf.sourceNativeType || copy.direction != value.direction || copy.evidence.level != EvidenceLevel.Declared
                || string.IsNullOrWhiteSpace(copy.evidence.origin) || value.evidence.level != EvidenceLevel.Declared
                || copy.direction is not (NativeParameterDirection.Input or NativeParameterDirection.Output)
                || copy.direction == NativeParameterDirection.Input && (copy.sourceParameter is null || copy.sourceParameter != parameter.sourceDeclaration)
                || copy.direction == NativeParameterDirection.Output && (copy.sourceParameter is not null || parameter.sourceDeclaration is not null
                    || Resolve(module, function.returnType).primitiveKind != "Void" || function.error is not null))
                return "A native value-copy projection lost its exact bridge, source declaration, storage or direction provenance.";
        }
        bool boolean = leaf.category == NativeTypeCategory.Primitive && leaf.primitiveKind == "Bool";
        if (boolean && (leaf.size != 1 || leaf.alignment != 1))
            return "A Boolean value borrow requires an actual one-byte, one-byte-aligned native Bool; integer Boolean conventions require an explicit adapter.";
        bool integral = leaf.primitiveKind is "Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong";
        bool floating = leaf.primitiveKind == "Float" && leaf.size == 4 || leaf.primitiveKind == "Double" && leaf.size == 8;
        if (leaf.category == NativeTypeCategory.Primitive && (leaf.size is not (1 or 2 or 4 or 8) || !(integral && leaf.isSigned is not null || floating || boolean)))
            return "This typed value projection requires a supported fixed scalar carrier; special scalar semantics need an explicit adapter.";
        if (leaf.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Record) || leaf.size is not > 0)
            return "A typed value parameter requires complete scalar or verified record storage; enums and pointer-to-pointer values need explicit adapters.";
        if (value.direction != NativeParameterDirection.Input)
        {
            InteropType view = module.GetType(element);
            HashSet<StableId> active = [];
            while (true)
            {
                if (!active.Add(view.id)) throw new InvalidOperationException("The typed value pointee graph is cyclic.");
                if (view.qualifier?.Contains("Const", StringComparison.Ordinal) == true) return "A const native value cannot be exposed as writable ref or out storage.";
                if (view.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) break;
                view = module.GetType(view.elementType!.Value);
            }
        }
        try
        {
            int alignment = GetAlignment(module, parameter);
            if (boolean && alignment != 1) return "A Boolean value borrow cannot reinterpret storage with a different native or requested alignment.";
            if (alignment > 8) return "Over-aligned typed values require an explicitly aligned native allocation adapter.";
        }
        catch (NotSupportedException failure) { return failure.Message; }
        return null;
    }

    /// <summary>Finds the full native alignment required by every pointee view and the explicit value contract.</summary>
    /// <param name="module">The complete native ABI.</param>
    /// <param name="parameter">The one-value pointer.</param>
    /// <returns>The power-of-two alignment to prove before passing the temporary address.</returns>
    public int GetAlignment(
        InteropModule module,
        InteropParameter parameter
    ) {
        int native = new FriendlyApiEligibilityAnalyzer().GetTextInputAlignment(module, parameter);
        int requested = parameter.singleValue?.requiredAlignment ?? native;
        if (requested <= 0 || (requested & (requested - 1)) != 0 || requested < native)
            throw new NotSupportedException("A typed value alignment must be a positive power of two at least as strict as its native pointee.");
        return requested;
    }

    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified && !string.IsNullOrWhiteSpace(evidence.origin);
    internal static InteropType Resolve(
        InteropModule module,
        StableId identity
    ) {
        HashSet<StableId> active = [];
        InteropType type = module.GetType(identity);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!active.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A typed value alias is incomplete or cyclic.");
            type = module.GetType(element);
        }
        return type;
    }
}
