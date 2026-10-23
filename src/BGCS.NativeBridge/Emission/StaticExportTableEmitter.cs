using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BGCS.Core.Interop;
using BGCS.Core.Requests;

namespace BGCS.NativeBridge.Emission;

/// <summary>Returns native adapter source and the exact neutral protocol expected by the consumer.</summary>
/// <param name="source">Complete C17 source referencing declarations from the supplied native headers.</param>
/// <param name="descriptor">The versioned static native export contract.</param>
public sealed record StaticExportTableSource(
    string source,
    StaticExportTableDescriptor descriptor
);

/// <summary>Emits a checked index-based static export adapter from exact final C ABI declarations.</summary>
public static class StaticExportTableEmitter
{
    /// <summary>Builds deterministic C17 source without reconstructing or guessing native function signatures.</summary>
    /// <param name="module">The final analyzed native ABI; function order defines adapter index order.</param>
    /// <param name="headers">Ordered explicit native declaration headers, with duplicates removed.</param>
    /// <returns>C source and its exact revision, count, pointer-width and ABI identity contract.</returns>
    /// <exception cref="NotSupportedException">An export lacks a fixed C prototype or a portable C declaration name.</exception>
    public static StaticExportTableSource Emit(
        InteropModule module,
        ImmutableArray<string> headers
    ) {
        ArgumentNullException.ThrowIfNull(module);
        if (headers.IsDefaultOrEmpty) throw new ArgumentException("A static export adapter requires exact native declaration headers.", nameof(headers));
        if (module.pointerSize is not (4 or 8)) throw new NotSupportedException("A static native adapter requires a known pointer width.");
        ImmutableArray<string> exports = module.functions.Select(function => function.exportName).ToImmutableArray();
        if (exports.Distinct(StringComparer.Ordinal).Count() != exports.Length) throw new ArgumentException("Static native export symbols are duplicated.", nameof(module));
        foreach (InteropFunction function in module.functions)
            if (!Identifier(function.exportName) || function.isVariadic || !function.hasPrototype)
                throw new NotSupportedException("Static export '" + function.exportName + "' requires an exact fixed C declaration and portable identifier.");
        string normalized = new(module.libraryId.Select(character => char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        if (normalized.Length == 0) throw new ArgumentException("The static library identity has no portable symbol characters.", nameof(module));
        string prefix = "bgcs_" + normalized;
        if (normalized != module.libraryId) prefix += "_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(module.libraryId)))[..8];
        string identity = AbiIdentity(module);
        StaticExportTableDescriptor descriptor = new(prefix, prefix + "_get_export", prefix + "_get_revision", prefix + "_get_count", prefix + "_get_abi_identity", prefix + "_get_pointer_size", identity, exports);
        if (exports.Intersect(new[] { descriptor.getExportSymbol, descriptor.getRevisionSymbol, descriptor.getCountSymbol, descriptor.getAbiIdentitySymbol, descriptor.getPointerSizeSymbol }, StringComparer.Ordinal).Any())
            throw new ArgumentException("A native export conflicts with its reserved static adapter entry point.", nameof(module));
        StringBuilder source = new("/* BGCS static export adapter, protocol revision 1. */\n#include <stdint.h>\n");
        foreach (string header in headers.Distinct(StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(header);
            if (header.Any(character => char.IsControl(character) || character is '"' or '<' or '>')) throw new ArgumentException("A native declaration header contains invalid include syntax.", nameof(headers));
            source.Append("#include \"").Append(header.Replace('\\', '/')).AppendLine("\"");
        }
        source.AppendLine("#if defined(_WIN32)\n#define BGCS_STATIC_EXPORT __declspec(dllexport)\n#else\n#define BGCS_STATIC_EXPORT __attribute__((visibility(\"default\"), used))\n#endif")
            .AppendLine("_Static_assert(sizeof(void (*)(void)) == sizeof(uintptr_t), \"BGCS function addresses require pointer-sized native storage\");")
            .Append("_Static_assert(sizeof(uintptr_t) == ").Append(module.pointerSize.ToString(CultureInfo.InvariantCulture)).AppendLine(", \"BGCS static target pointer width changed\");")
            .AppendLine("#ifdef __cplusplus\nextern \"C\" {\n#endif")
            .Append("BGCS_STATIC_EXPORT uint32_t ").Append(descriptor.getRevisionSymbol).AppendLine("(void) { return UINT32_C(1); }")
            .Append("BGCS_STATIC_EXPORT uint32_t ").Append(descriptor.getCountSymbol).Append("(void) { return UINT32_C(").Append(exports.Length.ToString(CultureInfo.InvariantCulture)).AppendLine("); }")
            .Append("BGCS_STATIC_EXPORT uint32_t ").Append(descriptor.getPointerSizeSymbol).AppendLine("(void) { return (uint32_t)sizeof(uintptr_t); }")
            .Append("BGCS_STATIC_EXPORT const char * ").Append(descriptor.getAbiIdentitySymbol).Append("(void) { return \"").Append(identity).AppendLine("\"; }")
            .Append("BGCS_STATIC_EXPORT uintptr_t ").Append(descriptor.getExportSymbol).AppendLine("(uint32_t index) {\n    switch (index) {");
        for (int index = 0; index < exports.Length; index++)
            source.Append("    case UINT32_C(").Append(index.ToString(CultureInfo.InvariantCulture)).Append("): return (uintptr_t)&").Append(exports[index]).AppendLine(";");
        source.AppendLine("    default: return (uintptr_t)0;\n    }\n}\n#ifdef __cplusplus\n}\n#endif\n#undef BGCS_STATIC_EXPORT");
        return new(source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), descriptor);
    }
    private static bool Identifier(string value) => !string.IsNullOrEmpty(value) && (char.IsAsciiLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    private static string AbiIdentity(InteropModule module)
    {
        byte[] shape = JsonSerializer.SerializeToUtf8Bytes(new
        {
            revision = 1,
            module.libraryId,
            module.targetId,
            module.pointerSize,
            module.byteOrder,
            functions = module.functions.Select(function => new
            {
                function.exportName,
                returnType = function.returnType.value,
                function.callingConvention,
                function.isVariadic,
                function.hasPrototype,
                parameters = function.parameters.Select(parameter => parameter.type.value).ToArray()
            }).ToArray(),
            types = module.types.OrderBy(type => type.id.value, StringComparer.Ordinal).Select(type => new
            {
                id = type.id.value,
                type.nativeName,
                type.category,
                type.size,
                type.alignment,
                type.primitiveKind,
                type.isSigned,
                type.qualifier,
                elementType = type.elementType?.value,
                type.arrayLength,
                returnType = type.returnType?.value,
                parameterTypes = type.parameterTypes.Select(parameter => parameter.value).ToArray(),
                type.callingConvention,
                type.isVariadic,
                type.hasPrototype,
                fields = type.fields.Select(field => new { type = field.type.value, field.offset, field.bitOffset, field.bitWidth }).ToArray()
            }).ToArray()
        });
        return Convert.ToHexStringLower(SHA256.HashData(shape));
    }
}
