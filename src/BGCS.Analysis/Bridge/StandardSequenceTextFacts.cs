using System;
using System.Linq;
using BGCS.Core.Bridge;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Preserves explicit encoding on counted native string inputs without confusing text with untyped buffers.</summary>
public static class StandardSequenceTextFacts
{
    /// <summary>Reads an optional exact text contract after the typed sequence operation has been independently verified.</summary>
    /// <param name="module">The original source and frozen family configuration.</param>
    /// <param name="group">The finite original argument and data/length channels.</param>
    /// <returns>Counted text for a standard path or explicitly encoded string, otherwise null.</returns>
    public static StringContract? Read(
        NativeBridgePlan module,
        BridgeArgumentGroup group
    ) {
        ArgumentNullException.ThrowIfNull(module); ArgumentNullException.ThrowIfNull(group);
        if (group.standardSequence is not StandardSequenceArgument sequence) return null;
        NativeType native = StandardLibraryFactReader.Resolve(module.source, group.nativeType);
        if (native.category == NativeTypeCategory.Reference) native = StandardLibraryFactReader.Resolve(module.source, native.elementType!.Value);
        CppFamilyBinding? binding = new StandardLibraryFactReader(module.familyBindings).FindBinding(module.source, native.id);
        NativeTextEncoding encoding = sequence.family == StandardValueKind.Path ? NativeTextEncoding.Utf8
            : sequence.family == StandardValueKind.String ? binding?.stringEncoding ?? NativeTextEncoding.Unknown : NativeTextEncoding.Unknown;
        if (encoding == NativeTextEncoding.Unknown) return null;
        NativeType unit = StandardLibraryFactReader.Resolve(module.source, sequence.elementType);
        int width = encoding switch { NativeTextEncoding.Utf8 => 1, NativeTextEncoding.Utf16 => 2, NativeTextEncoding.Utf32 => 4, _ => 0 };
        if (unit.category != NativeTypeCategory.Primitive || unit.size != width || unit.isSigned is null
            || sequence.materialization != StandardSequenceMaterialization.CopyValue)
            throw new InvalidOperationException("Counted text must preserve its exact native Unicode unit and nonretaining copy materialization.");
        return new(encoding, width, NativeLengthUnit.CodeUnits, NativeParameterDirection.Input,
            new(NativeLifetimeKind.Call, null, sequence.evidence), binding?.evidence ?? sequence.evidence)
        {
            byteOrder = module.source.byteOrder switch { NativeByteOrder.LittleEndian => NativeTextByteOrder.LittleEndian, NativeByteOrder.BigEndian => NativeTextByteOrder.BigEndian, _ => NativeTextByteOrder.Unknown },
            zeroTerminated = false,
            retainsPointer = false,
            allowsNull = true,
            lengthParameter = group.inputs.Single(input => input.name == "length").parameter,
            lengthDirection = NativeParameterDirection.Input,
            maximumCodeUnits = binding?.cStr?.maximumCodeUnits - 1 ?? StandardPathInputFacts.C_MAXIMUM_CODE_UNITS
        };
    }
}
