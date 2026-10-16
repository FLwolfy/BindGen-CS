using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Analysis.Bridge;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Cpp2C.Planning;

namespace BGCS.Cpp2C.Emission;

internal static partial class ConfiguredFamilyAssertionEmitter
{
    internal static void Emit(
        CppBridgeModule module,
        StringBuilder output
    ) {
        if (module.familyBindings.IsEmpty) return;
        StandardLibraryFactReader reader = new(module.familyBindings);
        StableId[] owners = module.exports.Where(export => export.operation.standardValue is not null)
            .Select(export => export.operation.ownerType!.Value).Distinct().Where(owner => reader.FindBinding(module.source, owner) is not null).ToArray();
        HashSet<string> guards = new(StringComparer.Ordinal)
        {
            "static_assert", "sizeof", "decltype", "typename", "const", "using", "std", "bool", "void", "type", "size_t", "uint64_t",
            "is_same", "is_integral", "is_unsigned", "is_convertible", "is_assignable", "is_const", "remove_cv", "remove_reference", "declval", "value",
            "value_type", "element_type", "size", "has_value", "push_back", "reserve", "data", "operator",
            "key_type", "mapped_type", "error_type", "begin", "end", "insert", "insert_or_assign", "first", "second", "error",
            "bgcs_iterator", "bgcs_iterator_end", "bgcs_index", "bgcs_instance", "index", "length_error",
            "is_default_constructible", "is_nothrow_destructible", "is_copy_constructible", "is_nothrow_copy_constructible", "is_nothrow_move_constructible",
            "get", "move", "generic_u8string", "u8string", "c_str", "is_pointer", "remove_pointer", "is_constructible", "rep", "period", "clock", "duration", "num", "den", "time_since_epoch", "count", "template",
            "bgcs_cstr_data", "bgcs_cstr_count", "vector", "static_cast", "nullptr", "invalid_argument", "out_of_range", "numeric_limits", "max"
        };
        foreach (BridgeExport export in module.exports)
        {
            foreach (BridgeParameter parameter in export.parameters) guards.Add(parameter.name);
            for (int group = 0; group < export.operation.argumentGroups.Length; group++)
            {
                string suffix = group.ToString(CultureInfo.InvariantCulture);
                guards.Add("bgcs_cstr_units_" + suffix);
                guards.Add("bgcs_sequence_type_" + suffix);
                guards.Add("bgcs_sequence_value_" + suffix);
                guards.Add("bgcs_sequence_index_" + suffix);
            }
        }
        for (int index = 0; index < owners.Length; index++)
        {
            foreach (string suffix in new[] { "owner", "element", "count", "read", "write", "mapped", "advance" }) guards.Add(Name(index, suffix));
            StandardValueOperation facts = reader.Read(module.source, owners[index])!;
            foreach (StableId type in facts.argumentTypes.Prepend(owners[index]))
                foreach (Match identifier in Regex.Matches(NativeTypeSpelling.Format(module.source, type), "[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant))
                    guards.Add(identifier.Value);
        }
        foreach (string guard in guards.Order(StringComparer.Ordinal))
            output.Append("#if defined(").Append(guard).Append(")\n#error BGCS configured family proof token is macro-defined: ").Append(guard).Append("\n#endif\n");
        for (int index = 0; index < owners.Length; index++)
        {
            StableId ownerId = owners[index];
            StandardValueOperation facts = reader.Read(module.source, ownerId)!;
            string owner = Name(index, "owner"), element = Name(index, "element"), count = Name(index, "count");
            output.Append("using ").Append(owner).Append(" = ").Append(NativeTypeSpelling.Format(module.source, ownerId)).Append(";\n");
            if (EmitOwnerVariantPathOrTime(module, reader.FindBinding(module.source, ownerId)!, facts, ownerId, owner, index, output, Check)) continue;
            output.Append("using ").Append(element).Append(" = ").Append(NativeTypeSpelling.Format(module.source, facts.argumentTypes[0])).Append(";\n");
            if (EmitCStr(module, reader, ownerId, owner, element, output, Check)) continue;
            if (EmitAssociativeOrExpected(module, facts, owner, element, index, output, Check)) continue;
            string member = facts.family == StandardValueKind.Span ? "element_type" : "value_type";
            Check("std::is_same<typename " + owner + "::" + member + ", " + element + ">::value", "exact native element type");
            if (facts.family == StandardValueKind.Optional)
            {
                Check("std::is_same<decltype(std::declval<const " + owner + "&>().has_value()), bool>::value", "optional presence result");
                Check("std::is_convertible<decltype(std::declval<const " + owner + "&>().value()), " + element + ">::value", "optional value result");
                continue;
            }
            output.Append("using ").Append(count).Append(" = typename std::remove_cv<typename std::remove_reference<decltype(std::declval<const ")
                .Append(owner).Append("&>().size())>::type>::type;\n");
            Check("std::is_integral<" + count + ">::value && std::is_unsigned<" + count + ">::value && !std::is_same<" + count + ", bool>::value && sizeof(" + count + ") <= sizeof(uint64_t)", "bounded unsigned native count");
            Check("std::is_convertible<decltype(std::declval<const " + owner + "&>()[std::declval<size_t>()]), " + element + ">::value", "readable native element");
            if (!IsConst(module, facts.argumentTypes[0]))
                Check("std::is_assignable<decltype(std::declval<" + owner + "&>()[std::declval<size_t>()]), " + element + ">::value", "writable native element");
            if (facts.family == StandardValueKind.Span)
                Check("std::is_same<decltype(std::declval<const " + owner + "&>().data()), " + element + "*>::value", "exact borrowed native element pointer");
            if (facts.family is StandardValueKind.String or StandardValueKind.Vector)
            {
                Check("std::is_same<decltype(std::declval<" + owner + "&>().push_back(std::declval<" + element + ">())), void>::value", "native append operation");
                Check("std::is_same<decltype(std::declval<" + owner + "&>().reserve(std::declval<size_t>())), void>::value", "native reserve operation");
            }
        }

        void Check(
            string expression,
            string detail
        ) => output.Append("static_assert(").Append(expression)
            .Append(", \"BGCS configured family requires ").Append(detail).Append("\");\n");
    }

    private static bool IsConst(
        CppBridgeModule module,
        StableId id
    ) {
        var type = module.source.GetType(id);
        return type.category == BGCS.Contracts.Native.NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
            || type.category is BGCS.Contracts.Native.NativeTypeCategory.Alias or BGCS.Contracts.Native.NativeTypeCategory.Qualified
                && type.elementType is StableId nested && IsConst(module, nested);
    }
    private static string Name(
        int index,
        string suffix
    ) => "bgcs_family_proof_" + index.ToString(CultureInfo.InvariantCulture) + "_" + suffix;
}
