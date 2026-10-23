using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;

namespace BGCS.Analysis.Layout;

/// <summary>Checks explicit scalar field copies separately from native storage equivalence and calling ABI.</summary>
public sealed class ExternalTypeContractValidator
{
    /// <summary>Validates complete lossless field copies through a separate generated raw record.</summary>
    /// <param name="module">The independently established native ABI.</param>
    /// <param name="projection">The exact field mapping and isolated managed observations.</param>
    /// <remarks>Managed offsets, layout and total size need not equal native storage because values are copied field by field.</remarks>
    public void ValidateProjection(
        InteropModule module,
        ExternalTypeProjection projection
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.managedShape is not null)
        {
            ValidateStaticProjection(module, projection);
            return;
        }
        ManagedTypeStorageFact managed = projection.managedStorage ?? throw new InvalidOperationException("An external type projection requires an isolated managed field probe.");
        if (managed.evidence.level != EvidenceLevel.Verified || managed.evidence.origin != "bgcs:managed-field-probe:v1"
            || managed.managedType != projection.managedType || !managed.isReferenceFree || managed.size <= 0 || managed.alignment <= 0)
            throw new InvalidOperationException("The external managed type lacks matching independently measured reference-free field facts.");
        _ = new ContentDigest(managed.probe.inputDigest.value);
        if (managed.probe.pointerSize is not (4 or 8) || string.IsNullOrWhiteSpace(managed.runtimeTypeIdentity))
            throw new InvalidOperationException("The external managed type probe identity is incomplete.");
        InteropType native = Resolve(module, projection.nativeType);
        if (native.category != NativeTypeCategory.Record || native.recordKind != "Struct" || native.size is null or <= 0 || native.alignment is null or <= 0
            || native.fields.IsDefaultOrEmpty || native.fields.Any(field => field.bitWidth is not null || field.offset is null))
            throw new NotSupportedException("External field copies require a complete ordinary struct with addressable scalar fields; unions, bitfields and flexible arrays need an explicit typed adapter.");
        if (projection.fields.IsDefault || projection.fields.Length != native.fields.Length
            || projection.fields.Select(field => field.nativeField).Distinct().Count() != projection.fields.Length
            || projection.fields.Select(field => field.managedField).Distinct(StringComparer.Ordinal).Count() != projection.fields.Length
            || managed.fields.IsDefault || managed.fields.Length != projection.fields.Length
            || managed.fields.Select(field => field.name).Distinct(StringComparer.Ordinal).Count() != managed.fields.Length)
            throw new InvalidOperationException("External field copies require one complete, unambiguous mapping of every native and managed instance field.");
        Dictionary<StableId, InteropField> nativeFields = native.fields.ToDictionary(field => field.id);
        Dictionary<string, ManagedFieldStorageFact> managedFields = managed.fields.ToDictionary(field => field.name, StringComparer.Ordinal);
        foreach (ExternalFieldProjection mapping in projection.fields)
        {
            if (!nativeFields.TryGetValue(mapping.nativeField, out InteropField? field)
                || !managedFields.TryGetValue(mapping.managedField, out ManagedFieldStorageFact? target))
                throw new InvalidOperationException("An external field mapping refers to a missing exact source identity or consumer field.");
            if (!target.isPublic || !target.isWritable || target.offset < 0 || target.size <= 0 || target.size > managed.size
                || target.offset > (long)managed.size - target.size)
                throw new NotSupportedException("External field copies require public writable scalar instance fields within the measured value.");
            InteropType source = Resolve(module, field.type);
            (ManagedScalarKind Kind, int Size) expected = Scalar(module, source);
            if (field.offset < 0 || expected.Size > native.size || field.offset > native.size - expected.Size)
                throw new NotSupportedException("An external native field extends outside its independently established record storage.");
            if (expected.Kind == ManagedScalarKind.NativeAddress && (managed.probe.pointerSize != module.pointerSize
                || module.byteOrder == NativeByteOrder.Unknown || managed.probe.isLittleEndian != (module.byteOrder == NativeByteOrder.LittleEndian)))
                throw new NotSupportedException("An external address field requires matching measured CLR/native pointer width and byte order; an integer of the same size is not an address contract.");
            if (target.scalarKind != expected.Kind || target.size != expected.Size || !TypeMatches(target.typeIdentity, target.scalarKind, target.size))
                throw new NotSupportedException($"External field '{mapping.managedField}' does not preserve native field '{field.name}' scalar kind, signedness and width.");
        }
        ManagedFieldStorageFact[] orderedFields = managed.fields.OrderBy(field => field.offset).ToArray();
        for (int index = 1; index < orderedFields.Length; index++)
            if (orderedFields[index - 1].offset + orderedFields[index - 1].size > orderedFields[index].offset)
                throw new NotSupportedException("External field copies cannot preserve independently valued fields in overlapping managed storage.");
    }

    /// <summary>Checks native and CLR storage equivalence without granting permission to pass the external type by value.</summary>
    /// <param name="module">The native ABI facts.</param>
    /// <param name="projection">The complete field mapping and managed measurement.</param>
    /// <remarks>Calling compatibility requires a separate runtime, architecture and import-specific proof; this method establishes storage only.</remarks>
    public void ValidateNativeStorage(
        InteropModule module,
        ExternalTypeProjection projection
    ) {
        ManagedTypeStorageFact managed = projection.managedStorage
            ?? throw new NotSupportedException("Static scalar field binding does not establish CLR size, offsets or native storage equivalence.");
        ValidateProjection(module, projection with { managedShape = null });
        InteropType native = Resolve(module, projection.nativeType);
        if (managed.layoutKind is not ("Sequential" or "Explicit") || native.size != managed.size || native.alignment != managed.alignment
            || managed.probe.pointerSize != module.pointerSize || managed.probe.isLittleEndian != (module.byteOrder == NativeByteOrder.LittleEndian)
            || module.byteOrder == NativeByteOrder.Unknown)
            throw new NotSupportedException("The external managed value does not have independently matching native storage size, alignment, byte order and pointer width.");
        foreach (ExternalFieldProjection mapping in projection.fields)
            if (native.fields.Single(field => field.id == mapping.nativeField).offset != managed.fields.Single(field => field.name == mapping.managedField).offset)
                throw new NotSupportedException("An external managed field offset differs from its exact native field.");
    }

    private static void ValidateStaticProjection(
        InteropModule module,
        ExternalTypeProjection projection
    ) {
        ManagedStaticTypeShape shape = projection.managedShape!;
        if (shape.managedType != projection.managedType || shape.evidence is not { level: EvidenceLevel.Declared, origin: "bgcs:managed-static-field-binding:v1" }
            || shape.fields.IsDefaultOrEmpty || projection.fields.IsDefault)
            throw new InvalidOperationException("An external scalar field copy requires complete matching static binding facts.");
        InteropType native = Resolve(module, projection.nativeType);
        if (native.category != NativeTypeCategory.Record || native.recordKind != "Struct" || native.size is null or <= 0
            || native.alignment is null or <= 0 || native.fields.IsDefaultOrEmpty || native.fields.Any(field => field.bitWidth is not null || field.offset is null))
            throw new NotSupportedException("Static external copies require a complete ordinary native struct with addressable scalar fields.");
        if (projection.fields.Length != native.fields.Length || shape.fields.Length != projection.fields.Length
            || projection.fields.Select(field => field.nativeField).Distinct().Count() != projection.fields.Length
            || projection.fields.Select(field => field.managedField).Distinct(StringComparer.Ordinal).Count() != projection.fields.Length
            || shape.fields.Select(field => field.name).Distinct(StringComparer.Ordinal).Count() != shape.fields.Length)
            throw new InvalidOperationException("Static external copies require one complete unique mapping of every native and managed instance field.");
        foreach (ExternalFieldProjection mapping in projection.fields)
        {
            InteropField? source = native.fields.SingleOrDefault(field => field.id == mapping.nativeField);
            ManagedStaticFieldShape? target = shape.fields.SingleOrDefault(field => field.name == mapping.managedField);
            if (source is null || target is null || !target.isPublic || !target.isWritable)
                throw new NotSupportedException("An external scalar field mapping must identify an accessible writable instance field.");
            (ManagedScalarKind kind, int size) = Scalar(module, Resolve(module, source.type));
            if (target.scalarKind != kind || target.size != size || !TypeMatches(target.typeIdentity, kind, size)
                || kind == ManagedScalarKind.NativeAddress && module.byteOrder == NativeByteOrder.Unknown
                || source.offset < 0 || size > native.size || source.offset > native.size - size)
                throw new NotSupportedException("An external field copy does not preserve native scalar kind, signedness and width, or native field extent: " + mapping.managedField);
        }
    }

    private static InteropType Resolve(
        InteropModule module,
        StableId id
    ) {
        HashSet<StableId> active = [];
        InteropType type = module.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!active.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("An external field type alias is incomplete or cyclic.");
            type = module.GetType(element);
        }
        return type;
    }

    private static (ManagedScalarKind, int) Scalar(
        InteropModule module,
        InteropType type
    ) {
        if (type.category == NativeTypeCategory.Pointer && type.size == module.pointerSize && type.elementType is StableId pointee
            && Resolve(module, pointee).category != NativeTypeCategory.Function)
            return (ManagedScalarKind.NativeAddress, module.pointerSize);
        if (type.category != NativeTypeCategory.Primitive || type.size is not (1 or 2 or 4 or 8))
            throw new NotSupportedException("External field copies require fixed-width scalar or data-address fields; callable pointers, enums and nested aggregates require explicit typed adapters.");
        int size = checked((int)type.size.Value);
        if (type.primitiveKind == "Float" && size == 4 || type.primitiveKind == "Double" && size == 8) return (ManagedScalarKind.BinaryFloat, size);
        if (type.primitiveKind is not ("Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong" or "LongLong" or "UnsignedLongLong")
            || type.isSigned is not bool signed)
            throw new NotSupportedException("The native scalar field has no supported exact external conversion semantics.");
        return (signed ? ManagedScalarKind.SignedInteger : ManagedScalarKind.UnsignedInteger, size);
    }

    private static bool TypeMatches(
        string type,
        ManagedScalarKind kind,
        int size
    ) => (kind, size) switch
    {
        (ManagedScalarKind.SignedInteger, 1) => type == "System.SByte",
        (ManagedScalarKind.UnsignedInteger, 1) => type == "System.Byte",
        (ManagedScalarKind.SignedInteger, 2) => type == "System.Int16",
        (ManagedScalarKind.UnsignedInteger, 2) => type == "System.UInt16",
        (ManagedScalarKind.SignedInteger, 4) => type == "System.Int32",
        (ManagedScalarKind.UnsignedInteger, 4) => type == "System.UInt32",
        (ManagedScalarKind.SignedInteger, 8) => type == "System.Int64",
        (ManagedScalarKind.UnsignedInteger, 8) => type == "System.UInt64",
        (ManagedScalarKind.BinaryFloat, 4) => type == "System.Single",
        (ManagedScalarKind.BinaryFloat, 8) => type == "System.Double",
        (ManagedScalarKind.NativeAddress, 4 or 8) => type is "System.IntPtr" or "System.UIntPtr"
            || !string.IsNullOrWhiteSpace(type) && type.EndsWith('*'),
        _ => false
    };
}
