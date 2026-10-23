using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using BGCS.Analysis.Bridge;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.NativeBridge.Planning;

internal sealed class DtoRecordPlanner
{
    private readonly NativeModule m_source;
    private readonly Func<StableId, BridgeType> m_storage;
    private readonly Func<string, StableId, string> m_name;
    private readonly Action<BridgeType> m_register;
    private readonly Func<StableId, bool> m_custom;
    private readonly Dictionary<StableId, bool> m_eligible = [];
    private readonly Dictionary<StableId, BridgeType> m_records = [];
    private readonly HashSet<StableId> m_active = [];

    internal DtoRecordPlanner(
        NativeModule source,
        Func<StableId, BridgeType> storage,
        Func<string, StableId, string> name,
        Action<BridgeType> register,
        Func<StableId, bool> custom
    ) { m_source = source; m_storage = storage; m_name = name; m_register = register; m_custom = custom; }

    internal BridgeType? Plan(StableId id)
    {
        NativeType native = StandardLibraryFactReader.Resolve(m_source, id);
        if (!Eligible(native.id)) return null;
        if (m_records.TryGetValue(native.id, out BridgeType? existing)) return existing;
        NativeDeclaration record = m_source.GetDeclaration(native.declaration!.Value);
        ImmutableArray<BridgeRecordField>.Builder fields = ImmutableArray.CreateBuilder<BridgeRecordField>();
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (NativeDeclaration field in record.children.Select(m_source.GetDeclaration).Where(field => field.kind == "Field" && !field.isStatic))
        {
            string name = FieldName(field.name);
            if (!names.Add(name)) throw new InvalidOperationException("DTO field identifiers collide after C name allocation.");
            fields.Add(new(field.id, name, m_storage(field.type!.Value).id, field.offset!.Value));
        }
        BridgeType representation = new(new("bridge-record:" + native.id.value), BridgeTypeKind.Record, m_name(record.qualifiedName + "_dto", native.id))
        { nativeType = native.id, recordStorage = new(native.size!.Value, native.alignment!.Value, fields.ToImmutable()) };
        m_records.Add(native.id, representation);
        m_register(representation);
        return representation;
    }

    private bool Eligible(StableId id)
    {
        if (m_custom(id)) return false;
        NativeType type = StandardLibraryFactReader.Resolve(m_source, id);
        if (m_eligible.TryGetValue(type.id, out bool result)) return result;
        if (!m_active.Add(type.id)) throw new InvalidOperationException("A native DTO contains a cyclic inline storage graph.");
        try
        {
            result = Check(type);
            m_eligible.Add(type.id, result);
            return result;
        }
        finally { m_active.Remove(type.id); }
    }

    private bool Check(NativeType type)
    {
        if (type.category == NativeTypeCategory.Primitive)
            return type.primitiveKind == "Bool" && type.size == 1 && type.alignment == 1
                || type.primitiveKind == "Float" && type.size == 4 || type.primitiveKind == "Double" && type.size == 8
                || type.size is 1 or 2 or 4 or 8 && type.isSigned is not null;
        if (type.category == NativeTypeCategory.Enumeration)
            return type.elementType is StableId underlying && Eligible(underlying)
                && type.declaration is StableId enumeration && m_source.GetDeclaration(enumeration).children.Select(m_source.GetDeclaration)
                    .Where(item => item.kind == "EnumItem").All(item => item.evaluatedIntegralValue is not null);
        if (type.category == NativeTypeCategory.Pointer)
            return type.size == m_source.pointerSize && type.alignment is > 0 && type.elementType is StableId pointee
                && StandardLibraryFactReader.Resolve(m_source, pointee).category is NativeTypeCategory.Record or NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration
                && StandardLibraryFactReader.Resolve(m_source, pointee).primitiveKind != "Bool";
        if (type.category == NativeTypeCategory.Array)
            return type.arrayLength is > 0 && type.elementType is StableId element && Eligible(element)
                && StandardLibraryFactReader.Resolve(m_source, element).size is long elementSize && type.size == checked(type.arrayLength.Value * elementSize);
        if (type.category != NativeTypeCategory.Record || type.declaration is not StableId declaration || type.size is not > 0
            || type.alignment is not > 0 || (type.alignment.Value & (type.alignment.Value - 1)) != 0) return false;
        NativeDeclaration record = m_source.GetDeclaration(declaration);
        if (record.role != "Struct" || record.isSystemDeclaration || !record.isCompleteDefinition || record.isAbstract
            || record.isStandardLayout != true || record.isTriviallyCopyable != true || !Accessible(record)
            || !record.baseClasses.IsEmpty || record.templateParameters.Length != 0
            || record.children.Select(m_source.GetDeclaration).Any(child => child.kind == "Function")) return false;
        long end = 0;
        foreach (NativeDeclaration field in record.children.Select(m_source.GetDeclaration).Where(field => field.kind == "Field" && !field.isStatic))
        {
            if (!Accessible(field) || field.bitWidth is not null || field.type is not StableId fieldType
                || field.offset is not long offset || offset < end || !Eligible(fieldType)
                || StandardLibraryFactReader.Resolve(m_source, fieldType).size is not long fieldSize || fieldSize <= 0) return false;
            end = checked(offset + fieldSize);
            if (end > type.size) return false;
        }
        return true;
    }

    private bool Accessible(NativeDeclaration declaration) => declaration.visibility is not ("Private" or "Protected")
        && (declaration.parent is not StableId parent || Accessible(m_source.GetDeclaration(parent)));

    private static string FieldName(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || name.Contains("__", StringComparison.Ordinal))
            throw new NotSupportedException("A DTO field requires a nonreserved C identifier or an explicit custom lowering.");
        return name is "auto" or "break" or "case" or "char" or "const" or "continue" or "default" or "do" or "double" or "else"
            or "enum" or "extern" or "float" or "for" or "goto" or "if" or "inline" or "int" or "long" or "register" or "restrict"
            or "return" or "short" or "signed" or "sizeof" or "static" or "struct" or "switch" or "typedef" or "union" or "unsigned"
            or "void" or "volatile" or "while" ? name + "_value" : name;
    }
}
