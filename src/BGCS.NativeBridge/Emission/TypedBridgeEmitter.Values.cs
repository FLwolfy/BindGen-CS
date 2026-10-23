using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Analysis.Bridge;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.NativeBridge.Planning;

namespace BGCS.NativeBridge.Emission;

/// <summary>Emits checked C ABI headers and typed C++ operations from the verified bridge model.</summary>
public sealed partial class TypedBridgeEmitter
{
    private static void EmitCodeUnitCopyAssertions(
        NativeBridgePlan module,
        StringBuilder source
    ) {
        BridgeExport[] copies = module.exports.Where(export => export.operation.standardValue?.action == StandardValueAction.CopyCodeUnits).ToArray();
        if (copies.Length == 0) return;
        HashSet<string> tokens = new(StringComparer.Ordinal) { "std", "static_assert", "is_same", "is_nothrow_destructible", "value", "basic_string", "char_traits", "allocator", "filesystem", "path", "numeric_limits", "max", "size_t", "static_cast", "reinterpret_cast", "alignof", "uintptr_t", "const", "int8_t", "uint8_t", "int16_t", "uint16_t", "int32_t", "uint32_t", "int64_t", "uint64_t", "nullptr", "length_error", "invalid_argument", "size", "data", "generic_u8string", "u8string", "c_str", "destination", "capacity", "bgcs_copy_count", "bgcs_copy_index", "bgcs_path_utf8", "bgcs_cstr_data", "bgcs_cstr_count" };
        foreach (BridgeExport copy in copies)
            foreach (StableId type in copy.operation.standardValue!.argumentTypes.Prepend(copy.operation.ownerType!.Value))
                foreach (Match token in Regex.Matches(NativeTypeSpelling.Format(module.source, type), "[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)) tokens.Add(token.Value);
        foreach (string token in tokens.Order(StringComparer.Ordinal))
            source.Append("#if defined(").Append(token).Append(")\n#error BGCS code-unit copy proof token is macro-defined: ").Append(token).Append("\n#endif\n");
        foreach (StableId owner in copies.Select(export => export.operation.ownerType!.Value).Distinct())
        {
            if (StandardValueReleaseFacts.Read(module.source, owner) is null) continue;
            NativeType native = StandardLibraryFactReader.Resolve(module.source, owner);
            StandardValueOperation family = new StandardLibraryFactReader().Read(module.source, native.id)!;
            string actual = NativeTypeSpelling.Format(module.source, native.id);
            string expected;
            if (family.family == StandardValueKind.Path) expected = "::std::filesystem::path";
            else
            {
                string unit = NativeTypeSpelling.Format(module.source, family.argumentTypes[0]);
                expected = "::std::basic_string<" + unit + ", ::std::char_traits<" + unit + ">, ::std::allocator<" + unit + ">>";
            }
            source.Append("static_assert(std::is_same<").Append(actual).Append(", ").Append(expected)
                .Append(">::value, \"BGCS creating-call release requires the exact default standard string/path allocation protocol\");\n")
                .Append("static_assert(std::is_nothrow_destructible<").Append(actual)
                .Append(">::value, \"BGCS creating-call release requires nonthrowing exact standard destruction\");\n");
        }
    }

    private static void EmitPathInputAssertions(
        NativeBridgePlan module,
        StringBuilder source
    ) {
        var inputs = module.exports.SelectMany(export => export.operation.argumentGroups
            .Select((
                group,
                index
            ) => (Export: export, Group: group, Index: index)))
            .Where(item => item.Group.standardSequence?.family == StandardValueKind.Path).ToArray();
        if (inputs.Length == 0) return;
        string[] tokens = ["std", "filesystem", "path", "u8string", "char8_t", "static_assert", "is_same", "is_constructible", "value", "sizeof", "static_cast", "size_t", "reserve", "push_back", "max", "numeric_limits", "length_error", "invalid_argument", "nullptr"];
        foreach (string token in tokens.Concat(inputs.SelectMany(item => item.Export.parameters.Select(parameter => parameter.name)))
            .Concat(inputs.SelectMany(item => new[] { "bgcs_path_units_" + item.Index, "bgcs_sequence_type_" + item.Index, "bgcs_sequence_value_" + item.Index, "bgcs_sequence_index_" + item.Index })).Distinct(StringComparer.Ordinal))
            source.Append("#if defined(").Append(token).Append(")\n#error BGCS standard path input proof token is macro-defined: ").Append(token).Append("\n#endif\n");
        foreach (var input in inputs)
        {
            var native = StandardLibraryFactReader.Resolve(module.source, input.Group.nativeType);
            if (native.category == NativeTypeCategory.Reference) native = StandardLibraryFactReader.Resolve(module.source, native.elementType!.Value);
            string actual = NativeTypeSpelling.Format(module.source, native.id);
            string unit = NativeTypeSpelling.Format(module.source, StandardPathInputFacts.ReadElement(module.source, native.id));
            source.Append("static_assert(std::is_same<").Append(actual).Append(", ::std::filesystem::path>::value, \"BGCS requires the actual standard filesystem path\");\n")
                .Append("static_assert(std::is_same<").Append(unit).Append(", char8_t>::value && sizeof(char8_t) == 1, \"BGCS requires actual one-byte char8_t UTF-8 units\");\n")
                .Append("static_assert(std::is_constructible<").Append(actual).Append(", std::u8string>::value, \"BGCS requires native standard UTF-8 path construction\");\n");
        }
    }

    private static void EmitValueAssertions(
        NativeBridgePlan module,
        StringBuilder source
    ) {
        StandardLibraryFactReader reader = new(module.familyBindings);
        int index = 0;
        foreach (StableId owner in module.exports.Where(export => export.operation.standardValue?.action is StandardValueAction.DurationCount or StandardValueAction.TimePointCount)
            .Select(export => export.operation.ownerType!.Value).Distinct())
        {
            NativeDurationPeriod? period = reader.ReadPeriod(module.source, owner);
            if (period is null) continue;
            string name = "bgcs_duration_period_" + (index++).ToString(CultureInfo.InvariantCulture);
            string native = NativeTypeSpelling.Format(module.source, owner);
            bool timePoint = reader.Read(module.source, owner)!.family == StandardValueKind.TimePoint;
            source.Append("\nusing ").Append(name).Append(" = typename ").Append(native).Append(timePoint ? "::duration::period;\n" : "::period;\n")
                .Append("static_assert(").Append(name).Append("::num == ").Append(period.numerator.ToString(CultureInfo.InvariantCulture)).Append("LL && ")
                .Append(name).Append("::den == ").Append(period.denominator.ToString(CultureInfo.InvariantCulture))
                .Append("LL, \"BGCS native duration period changed under final build arguments\");\n");
        }
    }
}
