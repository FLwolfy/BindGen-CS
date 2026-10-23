using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Safety;

/// <summary>Separates raw ABI validity from the evidence required by managed input conversions.</summary>
public sealed class FriendlyApiEligibilityAnalyzer
{
    /// <summary>Checks explicit bounds and lifetime before exposing a temporarily pinned managed buffer.</summary>
    /// <param name="module">The final native ABI.</param>
    /// <param name="function">The native callable containing the length parameter.</param>
    /// <param name="parameter">The pointer and its declared buffer contract.</param>
    /// <returns>A reason to omit the span projection, or null for an eligible bounded call.</returns>
    public string? GetBufferInputRestriction(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameter);
        BufferContract? buffer = parameter.buffer;
        if (buffer is null) return "No native buffer bounds and lifetime contract is declared.";
        if (parameter.text is not null || parameter.singleValue is not null || parameter.callback is not null)
            return "A buffer cannot simultaneously describe text, independent value or callback storage.";
        if (!Explicit(buffer.evidence) || !Explicit(buffer.lifetime.evidence)) return "Buffer and lifetime evidence must be explicitly declared or independently verified.";
        if (buffer.retainsPointer != false || buffer.lifetime.kind != NativeLifetimeKind.Call || function.asynchronous is not null)
            return "Pinned span input requires a nonretaining synchronous call lifetime.";
        if (buffer.direction is not (NativeParameterDirection.Input or NativeParameterDirection.Output or NativeParameterDirection.InputOutput)
            || buffer.allowsNull is null || buffer.lengthUnit is not (NativeLengthUnit.Bytes or NativeLengthUnit.Elements))
            return "Buffer direction, null allowance and byte or element bounds must be explicit.";
        if (buffer.lengthParameter is not StableId lengthId || buffer.capacityParameter is not null && buffer.capacityParameter != lengthId)
            return "This span projection requires one exact value length and no separate capacity protocol.";
        InteropParameter? length = null;
        foreach (InteropParameter candidate in function.parameters) if (candidate.id == lengthId) length = candidate;
        if (length is null || length.id == parameter.id || length.text is not null || length.buffer is not null || length.callback is not null || length.ownership is not null)
            return "The native buffer length must identify a separate scalar parameter.";
        InteropType integral = Resolve(length.type);
        if (integral.category != NativeTypeCategory.Primitive || integral.isSigned is null || integral.size is not (1 or 2 or 4 or 8)
            || integral.primitiveKind is "Bool" or "Float" or "Double" or "LongDouble" or "Void") return "The native buffer length requires a known integral carrier.";
        if (parameter.ownership is not null && (parameter.ownership.kind != NativeOwnershipKind.Borrowed
            || parameter.ownership.lifetime.kind != NativeLifetimeKind.Call || parameter.ownership.retainsPointer == true || !Explicit(parameter.ownership.evidence) || !Explicit(parameter.ownership.lifetime.evidence)))
            return "A pinned managed buffer cannot transfer ownership or outlive this call.";
        InteropType pointer = Resolve(parameter.type);
        if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element || Resolve(element).id != Resolve(buffer.elementType).id)
            return "The declared buffer element must match the native pointer element.";
        InteropType storage = Resolve(element);
        if (buffer.direction != NativeParameterDirection.Input)
        {
            InteropType view = module.GetType(element);
            HashSet<StableId> seen = [];
            while (true)
            {
                if (!seen.Add(view.id)) throw new InvalidOperationException("The native buffer pointee graph is cyclic.");
                if (view.qualifier?.Contains("Const", StringComparison.Ordinal) == true) return "A const native pointee cannot be declared as an output or writable span.";
                if (view.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) break;
                view = module.GetType(view.elementType!.Value);
            }
        }
        if (storage.size is not > 0 || storage.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration or NativeTypeCategory.Record)
            || storage.fields.Any(field => Resolve(field.type).category == NativeTypeCategory.Array && Resolve(field.type).arrayLength is null))
            return "Span elements require complete fixed native storage without flexible array tails.";
        try { _ = GetBufferInputAlignment(module, parameter); }
        catch (NotSupportedException failure) { return failure.Message; }
        return null;

        InteropType Resolve(StableId id)
        {
            HashSet<StableId> active = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!active.Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("The native buffer type graph is cyclic or incomplete.");
                type = module.GetType(next);
            }
            return type;
        }
    }

    /// <summary>Combines declared buffer alignment with every native pointee view.</summary>
    /// <param name="module">The final native ABI.</param>
    /// <param name="parameter">The pointer and its native buffer contract.</param>
    /// <returns>The power-of-two alignment checked before every pinned call.</returns>
    public int GetBufferInputAlignment(
        InteropModule module,
        InteropParameter parameter
    ) {
        int native = GetTextInputAlignment(module, parameter);
        int alignment = parameter.buffer?.requiredAlignment ?? native;
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0 || alignment < native)
            throw new NotSupportedException("Buffer alignment must be a positive power of two at least as strict as its native element.");
        return alignment;
    }
    /// <summary>Checks explicitly encoded terminated or counted text without guessing encoding or address retention.</summary>
    /// <param name="module">The final native ABI.</param>
    /// <param name="function">The callable containing the exact native length channel.</param>
    /// <param name="parameter">The parameter and its declared text contract.</param>
    /// <returns>A reason to omit the friendly conversion, or null when the supported conversion is eligible.</returns>
    public string? GetTextInputRestriction(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameter);
        StringContract? text = parameter.text;
        if (text is null) return "No native text encoding and lifetime contract is declared.";
        if (parameter.buffer is not null || parameter.singleValue is not null || parameter.callback is not null)
            return "Text cannot replace a competing buffer, value or callback conversion on the same address.";
        if (!Explicit(text.evidence) || !Explicit(text.lifetime.evidence)) return "Text and lifetime evidence must be explicitly declared or independently verified.";
        int width = text.encoding switch { NativeTextEncoding.Utf8 => 1, NativeTextEncoding.Utf16 => 2, NativeTextEncoding.Utf32 => 4, _ => 0 };
        if (width == 0 || text.codeUnitWidth != width) return "The declared text encoding and native code-unit width do not agree.";
        if (width > 1 && text.byteOrder is not (NativeTextByteOrder.Native or NativeTextByteOrder.LittleEndian or NativeTextByteOrder.BigEndian))
            return "Multibyte text input requires an explicit code-unit byte order.";
        if (text.lengthUnit is not (NativeLengthUnit.Bytes or NativeLengthUnit.CodeUnits)) return "Text input requires an explicit byte or code-unit length convention.";
        if (text.direction != NativeParameterDirection.Input || text.retainsPointer != false || text.lifetime.kind != NativeLifetimeKind.Call || function.asynchronous is not null)
            return "Temporary string input requires a read-only, nonretaining native call lifetime.";
        if (text.allowsNull is null || text.fixedLength is not null)
            return "Text input requires explicit null allowance and either termination or an exact scalar length channel.";
        if (text.lengthParameter is StableId lengthId)
        {
            if (text.zeroTerminated != false || text.lengthDirection != NativeParameterDirection.Input || text.lengthOutputCallBounded is not null || text.maximumCodeUnits <= 0)
                return "Counted text requires an explicit nonterminated input length and positive code-unit bound.";
            InteropParameter? length = function.parameters.SingleOrDefault(candidate => candidate.id == lengthId);
            if (length is null || length.id == parameter.id || length.text is not null || length.buffer is not null || length.callback is not null || length.ownership is not null || length.singleValue is not null)
                return "Counted text requires one separate plain scalar length parameter.";
            InteropType count = Underlying(length.type);
            if (count.category != NativeTypeCategory.Primitive || count.isSigned is null || count.size is not (1 or 2 or 4 or 8)
                || count.primitiveKind is not ("Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong"))
                return "Counted text length requires a known native integer carrier.";
            if (function.parameters.Any(candidate => candidate.id != parameter.id && candidate.text?.lengthParameter == lengthId
                || candidate.buffer is { } buffer && (buffer.lengthParameter == lengthId || buffer.capacityParameter == lengthId || buffer.writtenCountParameter == lengthId)))
                return "A counted text conversion requires an independent length channel; use Raw for shared text or buffer counts.";
        }
        else if (text.zeroTerminated != true || text.lengthDirection != NativeParameterDirection.Unknown || text.lengthOutputCallBounded is not null)
            return "Terminated text requires explicit zero termination without an unrelated length-direction protocol.";
        if (!CallBorrow(text.ownership) || !CallBorrow(parameter.ownership))
            return "Temporary managed-created input cannot transfer allocation ownership to native code.";
        InteropType pointer = Underlying(parameter.type);
        if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
            return "A text input must have a known native pointer element.";
        InteropType unit = Underlying(element);
        if (unit.category != NativeTypeCategory.Primitive || unit.size != width || unit.primitiveKind is not ("Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong" or "WChar" or "Char8" or "Char16" or "Char32"))
            return "The native text pointer requires integral code units with the declared encoding width.";
        try { _ = GetTextInputAlignment(module, parameter); }
        catch (NotSupportedException failure) { return failure.Message; }
        return null;

        static bool CallBorrow(OwnershipContract? ownership) => ownership is null || ownership.kind == NativeOwnershipKind.Borrowed
            && ownership.lifetime.kind == NativeLifetimeKind.Call && ownership.retainsPointer != true && Explicit(ownership.evidence) && Explicit(ownership.lifetime.evidence);

        InteropType Underlying(StableId id)
        {
            HashSet<StableId> active = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!active.Add(type.id) || type.elementType is not StableId next) throw new InvalidOperationException("The native text type graph is cyclic or incomplete.");
                type = module.GetType(next);
            }
            return type;
        }
    }

    /// <summary>Determines the complete declared pointee alignment, preserving alias alignment requirements.</summary>
    /// <param name="module">The immutable native ABI graph.</param>
    /// <param name="parameter">The known native text pointer.</param>
    /// <returns>The positive power-of-two allocation alignment required by every pointee view.</returns>
    /// <exception cref="NotSupportedException">The native alignment is unknown or invalid.</exception>
    public int GetTextInputAlignment(
        InteropModule module,
        InteropParameter parameter
    ) {
        InteropType type = module.GetType(parameter.type);
        HashSet<StableId> active = [];
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!active.Add(type.id) || type.elementType is not StableId underlying) throw new NotSupportedException("The text pointer type chain is incomplete or cyclic.");
            type = module.GetType(underlying);
        }
        if (type.category != NativeTypeCategory.Pointer || type.elementType is not StableId element) throw new NotSupportedException("The text input has no known pointer element.");
        type = module.GetType(element);
        active.Clear();
        int alignment = 1;
        while (true)
        {
            if (!active.Add(type.id) || type.alignment is not > 0 || (type.alignment.Value & (type.alignment.Value - 1)) != 0)
                throw new NotSupportedException("Every native text pointee view requires a known power-of-two allocation alignment.");
            alignment = Math.Max(alignment, type.alignment.Value);
            if (type.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) return alignment;
            if (type.elementType is not StableId next) throw new NotSupportedException("The native text pointee chain is incomplete.");
            type = module.GetType(next);
        }
    }

    private static bool Explicit(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified
        && !string.IsNullOrWhiteSpace(evidence.origin);
}
