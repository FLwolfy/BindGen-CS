using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks a finite standard sequence construction against its compiler-derived original type.</summary>
public sealed class StandardSequenceArgumentVerifier
{
    /// <summary>Rejects unproved borrowing, mismatched element carriers and incorrect compiler extents.</summary>
    /// <param name="module">The complete bridge model.</param>
    /// <param name="export">The additional input export.</param>
    /// <param name="group">The original argument and its data/length carriers.</param>
    public void Verify(
        NativeBridgePlan module,
        BridgeExport export,
        BridgeArgumentGroup group
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(group);
        StandardSequenceArgument sequence = group.standardSequence ?? throw new InvalidOperationException("A sequence group omitted its finite operation.");
        if (export.inputAdapterOf is null || group.expression is not null || group.inputs.Length != 2
            || group.inputs[0].name != "data" || group.inputs[1].name != "length")
            throw new InvalidOperationException("A finite sequence adapter requires exactly its data and length carriers without an arbitrary expression.");
        NativeModule source = module.source;
        NativeType native = StandardLibraryFactReader.Resolve(source, group.nativeType);
        bool reference = native.category == NativeTypeCategory.Reference;
        if (reference)
        {
            if (native.isRValueReference || !HasConst(native.elementType!.Value))
                throw new InvalidOperationException("A sequence reference must be an explicitly synchronous const lvalue reference.");
            native = StandardLibraryFactReader.Resolve(source, native.elementType.Value);
        }
        StandardLibraryFactReader reader = new(module.familyBindings);
        StandardValueOperation? actual = reader.Read(source, native.id);
        CppFamilyBinding? binding = reader.FindBinding(source, native.id);
        if (binding?.stringAccess == CppStringAccess.CStrNullTerminated && (binding.cStr?.constructorCopiesInput is not { level: EvidenceLevel.UserAsserted } copy
            || sequence.evidence != copy))
            throw new InvalidOperationException("A c_str sequence input lost its explicit copying-constructor evidence.");
        StableId? expectedElement = actual?.family == StandardValueKind.Path ? StandardPathInputFacts.ReadElement(source, native.id) : actual?.argumentTypes[0];
        if (actual is null || actual.family != sequence.family || expectedElement != sequence.elementType
            || sequence.family is not (StandardValueKind.String or StandardValueKind.Vector or StandardValueKind.Array or StandardValueKind.Span or StandardValueKind.Path)
            || !Enum.IsDefined(sequence.materialization))
            throw new InvalidOperationException("A sequence construction changed its original standard-library family or element identity.");
        bool borrow = sequence.family == StandardValueKind.Span;
        if (!borrow && HasConst(sequence.elementType))
            throw new InvalidOperationException("A copied sequence must have assignable native scalar elements.");
        if (sequence.materialization != (borrow ? StandardSequenceMaterialization.BorrowedView : StandardSequenceMaterialization.CopyValue)
            || sequence.evidence is null || sequence.evidence.level is not (EvidenceLevel.Declared or EvidenceLevel.UserAsserted)
            || string.IsNullOrWhiteSpace(sequence.evidence.origin))
            throw new InvalidOperationException("A sequence construction omitted explicit copy or synchronous nonretention evidence.");
        if (sequence.family == StandardValueKind.Path && sequence.evidence.level != EvidenceLevel.UserAsserted)
            throw new InvalidOperationException("Standard path input requires explicit call-bounded nonretention evidence.");
        if (ReadExtent(source, native.id, sequence.family) != sequence.extent)
            throw new InvalidOperationException("A sequence construction changed its compiler-derived native extent.");
        NativeType element = StandardLibraryFactReader.Resolve(source, sequence.elementType);
        if (element.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration) || element.primitiveKind == "Void"
            || borrow && (element.category == NativeTypeCategory.Enumeration || element.primitiveKind is "Bool" or "Char" or "WChar" or "Char8" or "Char16" or "Char32"))
            throw new InvalidOperationException("A sequence operation cannot borrow normalized distinct scalar storage or copy unproved object elements.");
        Dictionary<StableId, BridgeType> types = module.types.ToDictionary(type => type.id);
        BridgeParameter data = export.parameters.Single(parameter => parameter.id == group.inputs[0].parameter);
        BridgeParameter length = export.parameters.Single(parameter => parameter.id == group.inputs[1].parameter);
        BridgeType pointer = types[data.type];
        if (pointer.kind != BridgeTypeKind.Pointer || pointer.elementType is not StableId carrier
            || pointer.isConst != (!borrow || HasConst(sequence.elementType))
            || data.conversion != BridgeConversionKind.PointerCast || length.conversion != BridgeConversionKind.ScalarCast
            || types[length.type].kind != BridgeTypeKind.Scalar || types[length.type].cName != "uint64_t")
            throw new InvalidOperationException("A sequence operation changed its exact const element pointer or unsigned 64-bit element count.");
        BridgeType scalar = types[carrier];
        if (element.category == NativeTypeCategory.Enumeration)
        {
            if (scalar.kind != BridgeTypeKind.Enumeration || scalar.nativeType != element.id)
                throw new InvalidOperationException("A copied sequence enum lost its checked named carrier.");
        }
        else if (scalar.kind != BridgeTypeKind.Scalar || scalar.cName != ScalarName(element))
            throw new InvalidOperationException("A copied sequence element has an incompatible native C scalar carrier.");

        bool HasConst(StableId id)
        {
            NativeType type = source.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId nested && HasConst(nested);
        }
    }

    /// <summary>Reads fixed array/span extent and distinguishes the compiler-derived dynamic span sentinel.</summary>
    /// <param name="source">The detached original native facts.</param>
    /// <param name="type">The concrete record type identity.</param>
    /// <param name="family">The independently recognized sequence family.</param>
    /// <returns>A fixed extent, or null for dynamic sequence storage.</returns>
    public static ulong? ReadExtent(
        NativeModule source,
        StableId type,
        StandardValueKind family
    ) {
        if (family is not (StandardValueKind.Array or StandardValueKind.Span)) return null;
        NativeDeclaration declaration = source.GetDeclaration(StandardLibraryFactReader.Resolve(source, type).declaration!.Value);
        if (declaration.templateArguments.Length < 2 || declaration.templateArguments[1].kind != "AsInteger"
            || !ulong.TryParse(declaration.templateArguments[1].spelling, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value))
            throw new NotSupportedException("A fixed sequence adapter requires compiler-derived unsigned extent facts.");
        ulong dynamicExtent = source.pointerSize == 4 ? uint.MaxValue : ulong.MaxValue;
        return family == StandardValueKind.Span && value == dynamicExtent ? null : value;
    }

    private static string ScalarName(NativeType type) => type.primitiveKind switch
    {
        "Bool" when type.size == 1 => "uint8_t",
        "Float" when type.size == 4 => "float",
        "Double" when type.size == 8 => "double",
        _ when type.size is 1 or 2 or 4 or 8 && type.isSigned is not null => (type.isSigned.Value ? "int" : "uint") + (type.size.Value * 8).ToString(CultureInfo.InvariantCulture) + "_t",
        _ => throw new NotSupportedException("A sequence element has no checked scalar carrier.")
    };
}
