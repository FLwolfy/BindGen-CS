using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks exact DTO fields and storage without authorizing aggregate-by-value native calls.</summary>
public sealed class BridgeRecordVerifier
{
    /// <summary>Rejects missing compiler traits, changed fields and cyclic or incompatible inline carriers.</summary>
    /// <param name="module">The detached source and complete C storage graph.</param>
    public void Verify(CppBridgeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var types = module.types.ToDictionary(type => type.id);
        HashSet<StableId> checkedRecords = [];
        HashSet<StableId> active = [];
        HashSet<StableId> activeShapes = [];
        foreach (BridgeType type in module.types)
        {
            if (type.kind == BridgeTypeKind.Record) Record(type);
            else if (type.recordStorage is not null) throw new InvalidOperationException("Only DTO record carriers may declare record storage.");
            if (type.kind == BridgeTypeKind.FixedArray)
            {
                if (type.arrayLength is not > 0 || type.nativeType is not StableId original || !Match(type, Resolve(original)))
                    throw new InvalidOperationException("A DTO inline array lost its exact native element or extent.");
            }
            else if (type.arrayLength is not null) throw new InvalidOperationException("Only inline array carriers may declare an element count.");
        }

        void Record(BridgeType type)
        {
            if (checkedRecords.Contains(type.id)) return;
            if (!active.Add(type.id)) throw new InvalidOperationException("DTO inline storage is cyclic.");
            try
            {
                if (type.nativeType is not StableId original || type.recordStorage is not BridgeRecord storage
                    || storage.fields.IsDefault || storage.fields.Any(field => field is null) || storage.size <= 0 || storage.alignment <= 0
                    || (storage.alignment & (storage.alignment - 1)) != 0)
                    throw new InvalidOperationException("A DTO requires complete immutable storage and native identity.");
                NativeType native = Resolve(original);
                if (native.category != NativeTypeCategory.Record || native.declaration is not StableId declaration
                    || native.size != storage.size || native.alignment != storage.alignment)
                    throw new InvalidOperationException("DTO storage differs from its original native size or alignment.");
                NativeDeclaration owner = module.source.GetDeclaration(declaration);
                if (owner.role != "Struct" || !owner.isCompleteDefinition || owner.isAbstract || owner.isStandardLayout != true
                    || owner.isTriviallyCopyable != true || owner.isSystemDeclaration || !owner.baseClasses.IsEmpty
                    || owner.templateParameters.Length != 0 || !Accessible(owner)
                    || owner.children.Select(module.source.GetDeclaration).Any(child => child.kind == "Function"))
                    throw new InvalidOperationException("DTO storage requires compiler-established accessible complete standard-layout trivially-copyable struct facts.");
                NativeDeclaration[] fields = owner.children.Select(module.source.GetDeclaration).Where(field => field.kind == "Field" && !field.isStatic).ToArray();
                if (!fields.Select(field => field.id).SequenceEqual(storage.fields.Select(field => field.declaration))
                    || storage.fields.Select(field => field.name).Distinct(StringComparer.Ordinal).Count() != storage.fields.Length)
                    throw new InvalidOperationException("DTO storage changed or omitted the complete native field set.");
                long end = 0;
                for (int index = 0; index < fields.Length; index++)
                {
                    NativeDeclaration field = fields[index];
                    BridgeRecordField planned = storage.fields[index];
                    if (planned is null || !Regex.IsMatch(planned.name, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || planned.name.Contains("__", StringComparison.Ordinal)
                        || !Accessible(field) || field.bitWidth is not null || field.type is not StableId fieldType
                        || field.offset is not long offset || offset != planned.offset || offset < end
                        || !types.TryGetValue(planned.type, out BridgeType? carrier) || !Match(carrier, Resolve(fieldType))
                        || Resolve(fieldType).size is not long size || size <= 0)
                        throw new InvalidOperationException($"DTO field '{owner.qualifiedName}::{field.name}' lost its exact accessible native storage contract.");
                    end = checked(offset + size);
                    if (end > storage.size) throw new InvalidOperationException("A DTO field exceeds its complete native storage.");
                }
                checkedRecords.Add(type.id);
            }
            finally { active.Remove(type.id); }
        }

        bool Match(
            BridgeType carrier,
            NativeType native
        ) {
            if (carrier.kind == BridgeTypeKind.Record)
            {
                if (carrier.nativeType is not StableId dtoOriginal || Resolve(dtoOriginal).id != native.id) return false;
                Record(carrier);
                return native.category == NativeTypeCategory.Record;
            }
            if (carrier.kind == BridgeTypeKind.FixedArray)
            {
                if (!activeShapes.Add(carrier.id)) throw new InvalidOperationException("A DTO element storage graph is cyclic.");
                try
                {
                    return native.category == NativeTypeCategory.Array && native.arrayLength == carrier.arrayLength && carrier.arrayLength is > 0
                    && native.elementType is StableId element && carrier.elementType is StableId planned
                    && types.TryGetValue(planned, out BridgeType? child) && Match(child, Resolve(element));
                }
                finally { activeShapes.Remove(carrier.id); }
            }
            if (carrier.kind == BridgeTypeKind.Pointer)
            {
                if (!activeShapes.Add(carrier.id)) throw new InvalidOperationException("A DTO element storage graph is cyclic.");
                try
                {
                    if (native.category != NativeTypeCategory.Pointer || native.size != module.source.pointerSize
                        || native.elementType is not StableId element || carrier.elementType is not StableId planned
                        || !types.TryGetValue(planned, out BridgeType? child) || carrier.isConst != Const(element)) return false;
                    NativeType pointee = Resolve(element);
                    return child.kind == BridgeTypeKind.Handle
                        ? pointee.category == NativeTypeCategory.Record && child.nativeType is StableId pointerOriginal && Resolve(pointerOriginal).id == pointee.id
                        : Match(child, pointee);
                }
                finally { activeShapes.Remove(carrier.id); }
            }
            if (carrier.kind == BridgeTypeKind.Enumeration)
            {
                return native.category == NativeTypeCategory.Enumeration && carrier.nativeType is StableId enumOriginal && Resolve(enumOriginal).id == native.id
                    && native.elementType is StableId underlying && carrier.elementType is StableId planned
                    && types.TryGetValue(planned, out BridgeType? child) && Match(child, Resolve(underlying));
            }
            if (carrier.kind != BridgeTypeKind.Scalar || native.category != NativeTypeCategory.Primitive) return false;
            if (carrier.cName == "void") return native.primitiveKind == "Void";
            if (carrier.cName == "float") return native.primitiveKind == "Float" && native.size == 4;
            if (carrier.cName == "double") return native.primitiveKind == "Double" && native.size == 8;
            if (native.primitiveKind == "Bool") return native.size == 1 && native.alignment == 1 && carrier.cName == "uint8_t";
            bool signed = carrier.cName.StartsWith("int", StringComparison.Ordinal);
            int skip = signed ? 3 : 4;
            return carrier.cName.Length > skip + 2 && carrier.cName.EndsWith("_t", StringComparison.Ordinal)
                && int.TryParse(carrier.cName.AsSpan(skip, carrier.cName.Length - skip - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int bits)
                && native.size * 8 == bits && native.isSigned == signed;
        }
        NativeType Resolve(StableId id) => StandardLibraryFactReader.Resolve(module.source, id);
        bool Const(StableId id)
        {
            NativeType type = module.source.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId element && Const(element);
        }
        bool Accessible(NativeDeclaration declaration) => declaration.visibility is not ("Private" or "Protected")
            && (declaration.parent is not StableId parent || Accessible(module.source.GetDeclaration(parent)));
    }
}
