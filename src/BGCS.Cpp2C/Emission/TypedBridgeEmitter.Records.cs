using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BGCS.Analysis.Bridge;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using BGCS.Cpp2C.Planning;

namespace BGCS.Cpp2C.Emission;

/// <summary>Emits checked C ABI headers and typed C++ operations from the verified bridge model.</summary>
public sealed partial class TypedBridgeEmitter
{
    private static string RecordInputHelper(BridgeType record) => "bgcs_input_" + record.cName;

    private static void EmitRecordHeaders(
        CppBridgeModule module,
        Dictionary<StableId, BridgeType> types,
        StringBuilder header,
        string macro
    ) {
        BridgeType[] records = module.types.Where(type => type.kind == BridgeTypeKind.Record).ToArray();
        if (records.Length == 0) return;
        header.Append("#if defined(_WIN32)\n#define ").Append(macro).Append("_ALIGN(n) __declspec(align(n))\n#else\n#define ")
            .Append(macro).Append("_ALIGN(n) __attribute__((aligned(n)))\n#endif\n");
        foreach (BridgeType record in records) header.Append("typedef struct ").Append(record.cName).Append(' ').Append(record.cName).Append(";\n");
        HashSet<StableId> emitted = [];
        foreach (BridgeType record in records) Write(record);

        void Write(BridgeType record)
        {
            if (!emitted.Add(record.id)) return;
            BridgeRecord storage = record.recordStorage!;
            foreach (BridgeRecordField field in storage.fields)
            {
                BridgeType element = types[field.type];
                while (element.kind == BridgeTypeKind.FixedArray) element = types[element.elementType!.Value];
                if (element.kind == BridgeTypeKind.Record) Write(element);
            }
            header.Append("#pragma pack(push, 1)\nstruct ").Append(macro).Append("_ALIGN(").Append(storage.alignment).Append(") ").Append(record.cName).Append("\n{\n");
            long end = 0;
            int padding = 0;
            HashSet<string> names = storage.fields.Select(field => field.name).ToHashSet(StringComparer.Ordinal);
            foreach (BridgeRecordField field in storage.fields)
            {
                if (field.offset > end) Pad(field.offset - end);
                header.Append("    ").Append(FieldDeclaration(types[field.type], field.name)).Append(";\n");
                NativeDeclaration source = module.source.GetDeclaration(field.declaration);
                end = checked(field.offset + StandardLibraryFactReader.Resolve(module.source, source.type!.Value).size!.Value);
            }
            if (end < storage.size) Pad(storage.size - end);
            header.Append("};\n#pragma pack(pop)\n");
            void Pad(long size)
            {
                string name;
                do { name = "bgcs_padding_" + (padding++).ToString(CultureInfo.InvariantCulture); } while (!names.Add(name));
                header.Append("    uint8_t ").Append(name).Append('[').Append(size).Append("];\n");
            }
        }
        string FieldDeclaration(
            BridgeType type,
            string name
        ) {
            StringBuilder dimensions = new();
            while (type.kind == BridgeTypeKind.FixedArray)
            {
                dimensions.Append('[').Append(type.arrayLength!.Value).Append(']');
                type = types[type.elementType!.Value];
            }
            return TypeName(type) + " " + name + dimensions;
        }
        string TypeName(
            BridgeType type,
            bool qualified = false
        ) => type.kind == BridgeTypeKind.Pointer
            ? TypeName(types[type.elementType!.Value], type.isConst) + " *" + (qualified ? " const" : string.Empty)
            : (qualified ? "const " : string.Empty) + type.cName;
    }

    private static void EmitRecordHelpers(
        CppBridgeModule module,
        Dictionary<StableId, BridgeType> types,
        StringBuilder source
    ) {
        BridgeType[] records = module.types.Where(type => type.kind == BridgeTypeKind.Record).ToArray();
        if (records.Length == 0) return;
        string[] proofTokens = ["__builtin_offsetof", "static_assert", "sizeof", "alignof", "std", "is_standard_layout", "is_trivially_copyable", "is_same", "decltype", "declval", "value"];
        foreach (string token in proofTokens)
            source.Append("#if defined(").Append(token).Append(")\n#error BGCS DTO storage proof token is macro-defined: ").Append(token).Append("\n#endif\n");
        // Public headers may customize offsetof, and the Windows SDK macro is
        // not a constant expression under Clang. Use the compiler operation
        // independently for both the original record and its fixed C carrier.
        HashSet<StableId> inputs = module.exports.SelectMany(export => export.parameters)
            .Where(parameter => parameter.conversion == BridgeConversionKind.RecordCopy)
            .Select(parameter => types[parameter.type].elementType!.Value).ToHashSet();
        foreach (BridgeType record in records)
        {
            string native = NativeTypeSpelling.Format(module.source, record.nativeType!.Value);
            BridgeRecord storage = record.recordStorage!;
            source.Append("\nstatic_assert(std::is_standard_layout<").Append(native).Append(">::value && std::is_trivially_copyable<").Append(native)
                .Append(">::value, \"BGCS DTO storage requires standard layout and trivial copyability under final native arguments\");\n")
                .Append("static_assert(sizeof(").Append(native).Append(") == ").Append(storage.size).Append(" && sizeof(").Append(record.cName).Append(") == ").Append(storage.size)
                .Append(", \"BGCS DTO byte size changed\");\n")
                .Append("static_assert(alignof(").Append(native).Append(") == ").Append(storage.alignment).Append(" && alignof(").Append(record.cName).Append(") == ").Append(storage.alignment)
                .Append(", \"BGCS DTO alignment changed\");\n");
            foreach (BridgeRecordField field in storage.fields)
            {
                NativeDeclaration original = module.source.GetDeclaration(field.declaration);
                source.Append("static_assert(__builtin_offsetof(").Append(native).Append(", ").Append(original.name).Append(") == ").Append(field.offset)
                    .Append(" && __builtin_offsetof(").Append(record.cName).Append(", ").Append(field.name).Append(") == ").Append(field.offset)
                    .Append(", \"BGCS DTO field offset changed\");\n")
                    .Append("static_assert(std::is_same<decltype(std::declval<").Append(native).Append("&>().").Append(original.name).Append("), ")
                    .Append(FieldType(original.type!.Value)).Append(">::value, \"BGCS DTO field type changed\");\n");
            }
            if (!inputs.Contains(record.id)) continue;
            source.Append("[[maybe_unused]] static ").Append(native).Append(' ').Append(RecordInputHelper(record)).Append('(').Append(record.cName).Append(" const* input)\n{\n")
                .Append("    if (input == nullptr) throw std::invalid_argument(\"Missing DTO input storage\");\n")
                .Append("    if (reinterpret_cast<uintptr_t>(input) % ").Append(storage.alignment).Append(" != 0) throw std::invalid_argument(\"Unaligned DTO input storage\");\n")
                .Append("    ").Append(record.cName).Append(" bgcs_copy{};\n    std::memcpy(&bgcs_copy, static_cast<const void*>(input), sizeof(bgcs_copy));\n");
            int loop = 0;
            Validate(record, "bgcs_copy", "    ", ref loop);
            source.Append("    return std::bit_cast<").Append(native).Append(">(bgcs_copy);\n}\n");
        }

        void Validate(
            BridgeType carrier,
            string expression,
            string indent,
            ref int loop
        ) {
            if (carrier.kind == BridgeTypeKind.Record)
            {
                foreach (BridgeRecordField field in carrier.recordStorage!.fields)
                {
                    NativeDeclaration original = module.source.GetDeclaration(field.declaration);
                    Value(types[field.type], original.type!.Value, expression + "." + field.name, indent, ref loop);
                }
            }
        }
        void Value(
            BridgeType carrier,
            StableId original,
            string expression,
            string indent,
            ref int loop
        ) {
            NativeType native = StandardLibraryFactReader.Resolve(module.source, original);
            if (carrier.kind == BridgeTypeKind.FixedArray)
            {
                string index = "bgcs_dto_index_" + (loop++).ToString(CultureInfo.InvariantCulture);
                source.Append(indent).Append("for (size_t ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(carrier.arrayLength)
                    .Append("; ++").Append(index).Append(")\n").Append(indent).Append("{\n");
                Value(types[carrier.elementType!.Value], native.elementType!.Value, expression + "[" + index + "]", indent + "    ", ref loop);
                source.Append(indent).Append("}\n");
            }
            else if (carrier.kind == BridgeTypeKind.Record) Validate(carrier, expression, indent, ref loop);
            else if (native.primitiveKind == "Bool")
            {
                source.Append(indent).Append("static_assert(sizeof(bool) == 1 && std::bit_cast<uint8_t>(true) == 1 && std::bit_cast<uint8_t>(false) == 0, \"BGCS DTO boolean representation is unsupported\");\n")
                    .Append(indent).Append("if (").Append(expression).Append(" > 1) throw std::invalid_argument(\"A DTO boolean field must be zero or one\");\n");
            }
            else if (carrier.kind == BridgeTypeKind.Enumeration)
            {
                NativeDeclaration enumeration = module.source.GetDeclaration(native.declaration!.Value);
                string[] values = enumeration.children.Select(module.source.GetDeclaration).Where(value => value.kind == "EnumItem")
                    .Select(value => expression + " != " + carrier.cName + "_" + value.name).ToArray();
                source.Append(indent).Append("if (").Append(values.Length == 0 ? "true" : string.Join(" && ", values))
                    .Append(") throw std::invalid_argument(\"A DTO enum field requires a declared native value\");\n");
            }
        }
        string FieldType(StableId id)
        {
            NativeType type = module.source.GetType(id);
            if (type.category == NativeTypeCategory.Alias) return FieldType(type.elementType!.Value);
            NativeType unqualified = StandardLibraryFactReader.Resolve(module.source, id);
            if (unqualified.category == NativeTypeCategory.Array)
            {
                StringBuilder dimensions = new();
                bool isConst = false, isVolatile = false;
                while (type.category is NativeTypeCategory.Array or NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                {
                    if (type.category == NativeTypeCategory.Array) dimensions.Append('[').Append(type.arrayLength).Append(']');
                    else if (type.category == NativeTypeCategory.Qualified)
                    {
                        // Array-level CV qualifies the element type; preserve the
                        // exact pointer-level qualification at the final leaf.
                        isConst |= type.qualifier?.Contains("Const", StringComparison.Ordinal) == true;
                        isVolatile |= type.qualifier?.Contains("Volatile", StringComparison.Ordinal) == true;
                    }
                    type = module.source.GetType(type.elementType!.Value);
                }
                return NativeTypeSpelling.Format(module.source, type.id) + (isConst ? " const" : string.Empty) + (isVolatile ? " volatile" : string.Empty) + dimensions;
            }
            return NativeTypeSpelling.Format(module.source, id);
        }
    }
}
