using System;
using System.Linq;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Safety;

/// <summary>Validates bounded synchronous span protocols independently of output syntax.</summary>
public sealed class BufferProtocolAnalyzer
{
    /// <summary>Reports a supported protocol's omission reason without treating an unsupported convenience wrapper as an invalid raw ABI.</summary>
    /// <param name="module">The complete native ABI.</param>
    /// <param name="function">The containing function.</param>
    /// <param name="parameter">The explicitly annotated buffer pointer.</param>
    /// <returns>An omission reason, or null for a supported exact protocol.</returns>
    public string? GetRestriction(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) {
        try { _ = Analyze(module, function, parameter); return null; }
        catch (NotSupportedException failure) { return failure.Message; }
    }
    /// <summary>Validates one scalar bound and an optional separate output count pointer.</summary>
    /// <param name="module">The complete native ABI.</param>
    /// <param name="function">The native function containing every related identity.</param>
    /// <param name="parameter">The buffer pointer and explicit protocol.</param>
    /// <returns>The exact checked bound and optional written-count channels.</returns>
    /// <exception cref="NotSupportedException">The declared protocol lacks sufficient facts or uses an unimplemented shape.</exception>
    public BufferProtocolAnalysis Analyze(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(parameter);
        BufferContract buffer = parameter.buffer ?? throw new NotSupportedException("No explicit native buffer protocol is declared.");
        if (parameter.singleValue is not null) throw new NotSupportedException("A buffer cannot simultaneously be an independent single-value pointer.");
        StableId bound = buffer.capacityParameter ?? buffer.lengthParameter ?? throw new NotSupportedException("A span protocol requires an exact scalar length or capacity parameter.");
        if (buffer.capacityParameter is not null && buffer.lengthParameter is not null && buffer.capacityParameter != buffer.lengthParameter)
            throw new NotSupportedException("Distinct native input length and capacity parameters require an explicit initialized-extent adapter.");
        if (buffer.capacityParameter is not null && buffer.writtenCountParameter is null && buffer.direction == NativeParameterDirection.Input)
            throw new NotSupportedException("An input span requires an initialized length rather than an unspecified capacity-only extent.");
        BufferContract ordinary = buffer with { lengthParameter = bound, capacityParameter = null, writtenCountParameter = null };
        string? restriction = new FriendlyApiEligibilityAnalyzer().GetBufferInputRestriction(module, function, parameter with { buffer = ordinary });
        if (restriction is not null) throw new NotSupportedException(restriction);
        InteropParameter nativeBound = function.parameters.Single(candidate => candidate.id == bound);
        if (nativeBound.singleValue is not null) throw new NotSupportedException("A span bound cannot also be a projected value-pointer channel.");
        InteropType element = ValueParameterAnalyzer.Resolve(module, buffer.elementType);
        StableId? countType = null;
        int countAlignment = 0;
        if (buffer.writtenCountParameter is StableId count)
        {
            if (buffer.capacityParameter is null || buffer.direction == NativeParameterDirection.Input)
                throw new NotSupportedException("A written-count span requires an explicitly writable native buffer and scalar capacity.");
            InteropParameter? output = function.parameters.SingleOrDefault(candidate => candidate.id == count);
            if (output is null || count == parameter.id || count == bound || output.text is not null || output.buffer is not null || output.callback is not null || output.ownership is not null)
                throw new NotSupportedException("A written count must identify a distinct non-owning output integral pointer.");
            if (function.error?.valueParameter == count || function.error?.errorParameter == count)
                throw new NotSupportedException("A written-count output cannot simultaneously be a native status result or error channel.");
            if (output.singleValue is { } value && (value.direction != NativeParameterDirection.Output || new ValueParameterAnalyzer().GetRestriction(module, function, output) is not null))
                throw new NotSupportedException("A written-count value annotation conflicts with its exact output protocol.");
            InteropType pointer = ValueParameterAnalyzer.Resolve(module, output.type);
            if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId pointee)
                throw new NotSupportedException("A written count requires one known native integral-pointer level.");
            InteropType integral = ValueParameterAnalyzer.Resolve(module, pointee);
            if (integral.category != NativeTypeCategory.Primitive || integral.size is not (1 or 2 or 4 or 8) || integral.isSigned is null
                || integral.primitiveKind is not ("Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong"))
                throw new NotSupportedException("A written count requires a known fixed-width signed or unsigned integral carrier.");
            InteropType view = module.GetType(pointee);
            while (true)
            {
                if (view.qualifier?.Contains("Const", StringComparison.Ordinal) == true) throw new NotSupportedException("A const written-count pointer cannot receive an output value.");
                if (view.category is not (NativeTypeCategory.Alias or NativeTypeCategory.Qualified)) break;
                view = module.GetType(view.elementType!.Value);
            }
            if (function.parameters.Any(candidate => candidate.id != parameter.id && candidate.buffer?.writtenCountParameter == count))
                throw new NotSupportedException("Multiple buffers sharing one written-count pointer require an explicit coupled-output adapter.");
            countType = integral.id;
            countAlignment = new FriendlyApiEligibilityAnalyzer().GetTextInputAlignment(module, output);
            if (countAlignment > 8) throw new NotSupportedException("An over-aligned written count requires an explicitly aligned native allocation adapter.");
        }
        return new(parameter.id, bound, buffer.capacityParameter is not null, buffer.writtenCountParameter, element.id,
            checked((int)element.size!.Value), new FriendlyApiEligibilityAnalyzer().GetBufferInputAlignment(module, parameter), countType, countAlignment);
    }
}

/// <summary>Preserves exact native protocol channels without choosing managed syntax.</summary>
/// <param name="bufferParameter">The buffer's native identity.</param>
/// <param name="boundParameter">The scalar initialized length or capacity identity.</param>
/// <param name="boundIsCapacity">Whether the scalar bounds writable capacity.</param>
/// <param name="writtenCountParameter">An optional output count pointer identity.</param>
/// <param name="elementType">The canonical native element type.</param>
/// <param name="elementSize">The complete native element byte size.</param>
/// <param name="alignment">The exact address alignment to prove before pinning.</param>
/// <param name="writtenCountType">The optional canonical integral pointee type.</param>
/// <param name="writtenCountAlignment">The output-count address alignment to prove, or zero when no count exists.</param>
public sealed record BufferProtocolAnalysis(
    StableId bufferParameter,
    StableId boundParameter,
    bool boundIsCapacity,
    StableId? writtenCountParameter,
    StableId elementType,
    int elementSize,
    int alignment,
    StableId? writtenCountType,
    int writtenCountAlignment
);
