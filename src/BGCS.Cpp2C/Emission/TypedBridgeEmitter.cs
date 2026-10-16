using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Analysis.Bridge;

namespace BGCS.Cpp2C.Emission;

/// <summary>Emits candidate C declarations and C++ operations from detached typed bridge plans.</summary>
public sealed partial class TypedBridgeEmitter
{
    /// <summary>Writes the full candidate bridge without publishing or invoking a compiler.</summary>
    /// <param name="module">The frozen native operation plan.</param>
    /// <param name="sink">The candidate artifact sink.</param>
    /// <param name="fileStem">The checked filename stem, without an extension.</param>
    /// <param name="cancellationToken">Cancels cooperative emission.</param>
    /// <returns>The exact emitted artifact descriptors.</returns>
    public ImmutableArray<ArtifactDescriptor> Emit(
        CppBridgeModule module,
        IArtifactSink sink,
        string fileStem,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(sink);
        if (string.IsNullOrWhiteSpace(fileStem) || fileStem.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            throw new ArgumentException("The native artifact stem must contain only ASCII letters, digits, underscores and hyphens.", nameof(fileStem));
        Dictionary<StableId, BridgeType> types = module.types.ToDictionary(type => type.id);
        foreach (BridgeType type in module.types)
        {
            if (!Enum.IsDefined(type.kind)) throw new InvalidOperationException("A C representation has an unknown category.");
            if (type.kind is BridgeTypeKind.Pointer or BridgeTypeKind.FixedArray)
            {
                if (type.elementType is not StableId element || !types.ContainsKey(element)) throw new InvalidOperationException("A C pointer representation has no registered element.");
            }
            else if (type.kind == BridgeTypeKind.Scalar)
            {
                if (type.cName is not ("void" or "float" or "double" or "int8_t" or "uint8_t" or "int16_t" or "uint16_t" or "int32_t" or "uint32_t" or "int64_t" or "uint64_t"))
                    throw new NotSupportedException("A scalar C representation must use an explicitly supported fixed-width spelling.");
            }
            else if (!Regex.IsMatch(type.cName, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || type.cName.Contains("__", StringComparison.Ordinal))
                throw new InvalidOperationException($"Named C representation '{type.cName}' has no valid allocated identifier.");
            if (type.kind is BridgeTypeKind.Handle or BridgeTypeKind.Enumeration or BridgeTypeKind.Record or BridgeTypeKind.Function && type.nativeType is null)
                throw new InvalidOperationException("A named bridge representation has no source-language identity.");
        }
        if (module.types.Where(type => type.kind is BridgeTypeKind.Handle or BridgeTypeKind.Enumeration or BridgeTypeKind.Record or BridgeTypeKind.Function).Select(type => type.cName)
            .Append(module.errorTypeName).Distinct(StringComparer.Ordinal).Count() != module.types.Count(type => type.kind is BridgeTypeKind.Handle or BridgeTypeKind.Enumeration or BridgeTypeKind.Record or BridgeTypeKind.Function) + 1)
            throw new InvalidOperationException("Allocated C representation names conflict.");
        foreach (BridgeType type in module.types)
        {
            HashSet<StableId> active = [];
            BridgeType current = type;
            while (current.elementType is StableId element)
            {
                if (!active.Add(current.id) || !types.TryGetValue(element, out BridgeType? next))
                    throw new InvalidOperationException("The C representation graph is cyclic or missing an element.");
                current = next;
            }
            if (type.kind == BridgeTypeKind.Enumeration && (type.elementType is not StableId underlying || types[underlying].kind != BridgeTypeKind.Scalar
                || types[underlying].cName is "void" or "float" or "double"))
                throw new InvalidOperationException("A bridge enumeration requires a registered fixed-width integral element.");
        }
        if (module.exports.Select(export => export.id).Distinct().Count() != module.exports.Length
            || module.exports.Select(export => export.name).Append(module.errorMessageExport).Append(module.errorReleaseExport).Distinct(StringComparer.Ordinal).Count() != module.exports.Length + 2)
            throw new InvalidOperationException("Bridge export identities or support symbols conflict.");
        foreach (BridgeExport export in module.exports)
        {
            if (!Regex.IsMatch(export.name, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || export.name.Contains("__", StringComparison.Ordinal))
                throw new InvalidOperationException("A bridge export has no valid allocated C identifier.");
            if (export.parameters.Select(parameter => parameter.id).Distinct().Count() != export.parameters.Length
                || export.parameters.Select(parameter => parameter.name).Append("bgcs_result").Append("bgcs_error").Distinct(StringComparer.Ordinal).Count() != export.parameters.Length + 2)
                throw new InvalidOperationException("A bridge export contains duplicate or reserved parameter identities.");
            foreach (BridgeParameter parameter in export.parameters)
                if (!types.ContainsKey(parameter.type) || !Enum.IsDefined(parameter.conversion) || !Regex.IsMatch(parameter.name, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                    throw new InvalidOperationException("A bridge parameter has no valid representation or conversion.");
            if (export.resultType is StableId result && !types.ContainsKey(result)) throw new InvalidOperationException("A bridge result has no registered representation.");
            if (export.resultRelease is StableId release && !module.exports.Any(candidate => candidate.id == release)) throw new InvalidOperationException("An owned bridge result has no registered release export.");
            if (export.operation.instanceParameter is StableId instance && !export.parameters.Any(parameter => parameter.id == instance))
                throw new InvalidOperationException("A bridge action omitted its explicit instance parameter.");
        }
        new CppBridgeOperationVerifier().Verify(module);
        StringBuilder header = new("/* Generated by BGCS. Edit inputs instead of this file. */\n#pragma once\n#include <stdint.h>\n#include <stddef.h>\n\n");
        string apiMacro = module.exportPrefix.TrimEnd('_').ToUpperInvariant() + "_API";
        string callMacro = module.exportPrefix.TrimEnd('_').ToUpperInvariant() + "_CALL";
        header.Append("#if defined(_WIN32)\n#define ").Append(apiMacro).Append(" __declspec(dllexport)\n#define ").Append(callMacro).Append(" __cdecl\n#else\n#define ")
            .Append(apiMacro).Append(" __attribute__((visibility(\"default\")))\n#define ").Append(callMacro).Append("\n#endif\n\n#ifdef __cplusplus\nextern \"C\" {\n#endif\n\n");
        foreach (BridgeType handle in module.types.Where(type => type.kind == BridgeTypeKind.Handle))
            header.Append("typedef struct ").Append(handle.cName).Append(' ').Append(handle.cName).Append(";\n");
        foreach (BridgeType enumeration in module.types.Where(type => type.kind == BridgeTypeKind.Enumeration))
        {
            header.Append("typedef ").Append(TypeName(types[enumeration.elementType!.Value])).Append(' ').Append(enumeration.cName).Append(";\n");
            NativeType native = module.source.GetType(enumeration.nativeType!.Value);
            NativeDeclaration declaration = module.source.GetDeclaration(native.declaration!.Value);
            NativeType underlying = module.source.GetType(native.elementType!.Value);
            while (underlying.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) underlying = module.source.GetType(underlying.elementType!.Value);
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (NativeDeclaration value in declaration.children.Select(module.source.GetDeclaration).Where(child => child.kind == "EnumItem"))
            {
                string identifier = Regex.Replace(value.name, "[^A-Za-z0-9_]", "_", RegexOptions.CultureInvariant);
                if (!names.Add(identifier)) throw new InvalidOperationException("Native enum constant names collide after C identifier allocation.");
                if (!BigInteger.TryParse(value.evaluatedIntegralValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger number))
                    throw new NotSupportedException("A bridge enum constant requires an independently evaluated native integral value.");
                string literal = number == long.MinValue ? "(-9223372036854775807LL - 1LL)" : number.ToString(CultureInfo.InvariantCulture) + (underlying.isSigned == true ? "LL" : "ULL");
                header.Append("#define ").Append(enumeration.cName).Append('_').Append(identifier).Append(" ((").Append(enumeration.cName).Append(")").Append(literal).Append(")\n");
            }
        }
        HashSet<StableId> emittedFunctions = [];
        foreach (BridgeType callback in module.types.Where(type => type.kind == BridgeTypeKind.Function)) EmitFunction(callback);
        EmitRecordHeaders(module, types, header, apiMacro);
        header.Append("typedef struct ").Append(module.errorTypeName).Append(' ').Append(module.errorTypeName).Append(";\n\n");
        foreach (BridgeExport export in module.exports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            header.Append(Signature(export)).Append(";\n");
        }
        header.Append(apiMacro).Append(" const char* ").Append(callMacro).Append(' ').Append(module.errorMessageExport).Append("(const ").Append(module.errorTypeName).Append("* error);\n")
            .Append(apiMacro).Append(" void ").Append(callMacro).Append(' ').Append(module.errorReleaseExport).Append('(').Append(module.errorTypeName).Append("* error);\n\n#ifdef __cplusplus\n}\n#endif\n");
        StringBuilder source = new("/* Generated by BGCS. */\n#include \"../include/");
        source.Append(fileStem).Append(".h\"\n#include <cstdlib>\n#include <cstring>\n#include <exception>\n#include <limits>\n#include <memory>\n#include <new>\n#include <stdexcept>\n#include <string>\n#include <type_traits>\n#include <utility>\n#include <vector>\n");
        if (module.languageRequirements.Any(requirement => requirement.feature == "bridge.dto-copy")) source.Append("#include <bit>\n");
        source.Append(module.preamble).Append('\n');
        foreach (string nativeHeader in module.headers)
        {
            if (nativeHeader.Any(character => character is '\n' or '\r' or '"' or '\0')) throw new ArgumentException("A native header cannot be represented as a quoted include.", nameof(module));
            source.Append("#include \"").Append(nativeHeader.Replace('\\', '/')).Append("\"\n");
        }
        source.Append(module.postamble).Append('\n');
        foreach (ControlledSourceFragment fragment in module.sourceFragments)
            source.Append(fragment.text).Append('\n');
        ConfiguredFamilyAssertionEmitter.Emit(module, source);
        EmitRecordHelpers(module, types, source);
        EmitValueAssertions(module, source);
        EmitPathInputAssertions(module, source);
        EmitCodeUnitCopyAssertions(module, source);
        source.Append("\nstruct ").Append(module.errorTypeName).Append(" { char message[512]; };\n\n")
            .Append("static int32_t bgcs_capture_error(").Append(module.errorTypeName).Append("** output, const char* message, int32_t status) noexcept\n{\n")
            .Append("    if (output != nullptr)\n    {\n        void* storage = std::malloc(sizeof(").Append(module.errorTypeName).Append("));\n")
            .Append("        auto* error = storage == nullptr ? nullptr : ::new (storage) ").Append(module.errorTypeName).Append("{};\n")
            .Append("        if (error != nullptr)\n        {\n            size_t index = 0;\n            if (message != nullptr)\n                for (; index + 1 < sizeof(error->message) && message[index] != '\\0'; ++index) error->message[index] = message[index];\n")
            .Append("            error->message[index] = '\\0';\n            *output = error;\n        }\n    }\n    return status;\n}\n\n");
        foreach (BridgeExport export in module.exports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.Append(Signature(export)).Append("\n{\n    if (bgcs_error != nullptr) *bgcs_error = nullptr;\n");
            if (export.resultType is not null)
            {
                source.Append("    if (bgcs_result == nullptr) return bgcs_capture_error(bgcs_error, \"Missing result storage\", 2);\n");
                if (export.resultConversion == BridgeConversionKind.RecordCopy)
                    source.Append("    if (reinterpret_cast<uintptr_t>(bgcs_result) % ").Append(types[export.resultType.Value].recordStorage!.alignment)
                        .Append(" != 0) return bgcs_capture_error(bgcs_error, \"Unaligned DTO result storage\", 2);\n    std::memset(static_cast<void*>(bgcs_result), 0, sizeof(*bgcs_result));\n");
                else source.Append("    *bgcs_result = {};\n");
            }
            foreach (BridgeParameter parameter in export.parameters.Where(parameter => parameter.id == export.operation.instanceParameter || parameter.conversion is BridgeConversionKind.Dereference or BridgeConversionKind.Move))
            {
                if (export.operation.kind == BridgeOperationKind.Destroy) continue;
                source.Append("    if (").Append(parameter.name).Append(" == nullptr) return bgcs_capture_error(bgcs_error, \"Missing native object or reference\", 2);\n");
            }
            source.Append("    try\n    {\n");
            string? owner = export.operation.ownerType is StableId ownerType ? NativeName(ownerType) : null;
            string? self = export.parameters.FirstOrDefault(parameter => parameter.id == export.operation.instanceParameter)?.name;
            if (owner is not null && self is not null)
            {
                bool isConst = types[export.parameters.First(parameter => parameter.id == export.operation.instanceParameter).type].isConst;
                source.Append("        auto* bgcs_instance = reinterpret_cast<").Append(isConst ? "const " : string.Empty).Append(owner).Append("*>(").Append(self).Append(");\n");
            }
            string arguments = export.operation.kind == BridgeOperationKind.StandardValue ? string.Empty
                : export.operation.argumentGroups.IsEmpty
                    ? string.Join(", ", export.parameters.Where(parameter => parameter.id != export.operation.instanceParameter).Select(ConvertArgument))
                    : string.Join(", ", export.operation.argumentGroups.Select(group => ConvertGroup(export, group)));
            NativeDeclaration? callable = export.operation.declaration is StableId declaration ? module.source.GetDeclaration(declaration) : null;
            string invocation = export.operation.kind switch
            {
                BridgeOperationKind.FreeCall or BridgeOperationKind.StaticCall => "::" + callable!.qualifiedName + "(" + arguments + ")",
                BridgeOperationKind.InstanceCall => "bgcs_instance->" + callable!.name + "(" + arguments + ")",
                BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct => "new " + owner + "(" + arguments + ")",
                BridgeOperationKind.BaseCast => "static_cast<" + NativeName(export.operation.castTarget!.Value) + "*>(bgcs_instance)",
                BridgeOperationKind.CheckedDerivedCast => "dynamic_cast<" + NativeName(export.operation.castTarget!.Value) + "*>(bgcs_instance)",
                BridgeOperationKind.Destroy => "delete bgcs_instance",
                BridgeOperationKind.StandardValue => StandardInvocation(export),
                _ => throw new NotSupportedException("The native operation has no typed emitter.")
            };
            if (export.operation.invocationExpression is ControlledSourceFragment invocationExpression)
                invocation = "(" + new ControlledSourceFragmentVerifier().Substitute(invocationExpression, new Dictionary<string, string>(StringComparer.Ordinal) { ["invocation"] = invocation }) + ")";
            if (export.operation.kind == BridgeOperationKind.Destroy)
                source.Append("        static_assert(std::is_nothrow_destructible<").Append(owner).Append(">::value, \"BGCS owned releases require a nonthrowing destructor\");\n");
            if (export.operation.kind == BridgeOperationKind.DefaultConstruct)
                source.Append("        static_assert(std::is_default_constructible<").Append(owner).Append(">::value, \"BGCS default construction must be valid under final native build arguments\");\n");
            if (export.resultType is StableId resultType)
            {
                string result = TypeName(types[resultType]);
                if (export.resultConversion == BridgeConversionKind.OwnedValue)
                {
                    string nativeResult = export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct ? owner! : NativeName(export.nativeResultType!.Value);
                    string allocation = export.operation.kind is BridgeOperationKind.Construct or BridgeOperationKind.DefaultConstruct ? invocation : "new " + nativeResult + "(" + invocation + ")";
                    source.Append("        std::unique_ptr<").Append(nativeResult).Append("> bgcs_owned(").Append(allocation).Append(");\n")
                        .Append("        *bgcs_result = reinterpret_cast<").Append(result).Append(">(bgcs_owned.release());\n");
                }
                else if (export.resultConversion == BridgeConversionKind.RecordCopy)
                    source.Append("        auto bgcs_native_result = (").Append(invocation).Append(");\n        std::memcpy(static_cast<void*>(bgcs_result), &bgcs_native_result, sizeof(bgcs_native_result));\n");
                else
                {
                    if (export.resultConversion == BridgeConversionKind.SourceExpression)
                        source.Append("        auto&& bgcs_native_result = (").Append(invocation).Append(");\n");
                    string converted = export.resultConversion switch
                    {
                        BridgeConversionKind.SourceExpression => "(" + new ControlledSourceFragmentVerifier().Substitute(export.resultExpression!, new Dictionary<string, string>(StringComparer.Ordinal)
                        { ["value"] = "bgcs_native_result", ["resultType"] = result }) + ")",
                        BridgeConversionKind.ScalarCast => "static_cast<" + result + ">(" + invocation + ")",
                        BridgeConversionKind.ReferenceAddress => "reinterpret_cast<" + result + ">(&(" + invocation + "))",
                        BridgeConversionKind.PointerCast => "reinterpret_cast<" + result + ">(" + invocation + ")",
                        _ => throw new NotSupportedException("The native result has no typed conversion.")
                    };
                    source.Append("        *bgcs_result = ").Append(converted).Append(";\n");
                }
            }
            else source.Append("        ").Append(invocation).Append(";\n");
            source.Append("        return 0;\n    }\n    catch (const std::exception& exception) { return bgcs_capture_error(bgcs_error, exception.what(), 1); }\n")
                .Append("    catch (...) { return bgcs_capture_error(bgcs_error, \"Native exception\", 1); }\n}\n\n");
        }
        source.Append(apiMacro).Append(" const char* ").Append(callMacro).Append(' ').Append(module.errorMessageExport).Append("(const ").Append(module.errorTypeName).Append("* error)\n{\n    return error == nullptr ? nullptr : error->message;\n}\n\n")
            .Append(apiMacro).Append(" void ").Append(callMacro).Append(' ').Append(module.errorReleaseExport).Append('(').Append(module.errorTypeName).Append("* error)\n{\n    std::free(static_cast<void*>(error));\n}\n");
        return [Write("include/" + fileStem + ".h", "text/x-c", header.ToString()), Write("src/" + fileStem + ".cpp", "text/x-c++", source.ToString())];

        string Signature(BridgeExport export)
        {
            List<string> parameters = export.parameters.Select(parameter => TypeName(types[parameter.type]) + " " + parameter.name).ToList();
            if (export.resultType is StableId result) parameters.Add(TypeName(types[result]) + " *bgcs_result");
            parameters.Add(module.errorTypeName + " **bgcs_error");
            return apiMacro + " int32_t " + callMacro + " " + export.name + "(" + string.Join(", ", parameters) + ")";
        }

        string TypeName(
            BridgeType type,
            bool qualified = false
        ) => type.kind == BridgeTypeKind.Pointer
            ? TypeName(types[type.elementType!.Value], type.isConst) + " *" + (qualified ? " const" : string.Empty)
            : (qualified ? "const " : string.Empty) + type.cName;

        string NativeName(StableId id)
        {
            return BGCS.Cpp2C.Planning.NativeTypeSpelling.Format(module.source, id);
        }

        void EmitFunction(BridgeType function)
        {
            if (!emittedFunctions.Add(function.id)) return;
            foreach (StableId dependency in function.parameterTypes.Prepend(function.returnType!.Value))
            {
                BridgeType element = types[dependency];
                while (element.kind == BridgeTypeKind.Pointer) element = types[element.elementType!.Value];
                if (element.kind == BridgeTypeKind.Function) EmitFunction(element);
            }
            string parameters = string.Join(", ", function.parameterTypes.Select(parameter => TypeName(types[parameter]))
                .Concat(function.isVariadic ? ["..."] : []));
            if (parameters.Length == 0) parameters = "void";
            header.Append("typedef ").Append(TypeName(types[function.returnType!.Value])).Append(" (").Append(callMacro).Append(' ')
                .Append(function.cName).Append(")(").Append(parameters).AppendLine(");");
        }

        string StandardInvocation(BridgeExport export)
        {
            StandardValueOperation standard = export.operation.standardValue!;
            StandardLibraryFactReader familyReader = new(module.familyBindings);
            CppFamilyBinding? configured = familyReader.FindBinding(module.source, export.operation.ownerType!.Value);
            bool cStr = configured?.stringAccess == CppStringAccess.CStrNullTerminated;
            if (export.operation.instanceParameter is not null && familyReader.FindBinding(module.source, export.operation.ownerType!.Value) is not null
                && standard.family is StandardValueKind.Array or StandardValueKind.Span
                && StandardSequenceArgumentVerifier.ReadExtent(module.source, export.operation.ownerType.Value, standard.family) is ulong fixedExtent)
                source.Append("        if (static_cast<uint64_t>(bgcs_instance->size()) != ").Append(fixedExtent.ToString(CultureInfo.InvariantCulture))
                    .Append("ULL) throw std::length_error(\"BGCS configured family contradicts its native fixed extent\");\n");
            BridgeParameter[] arguments = export.parameters.Where(parameter => parameter.id != export.operation.instanceParameter).ToArray();
            string owner = NativeName(export.operation.ownerType!.Value);
            if (cStr && standard.action is StandardValueAction.Count or StandardValueAction.GetAt or StandardValueAction.CopyCodeUnits)
            {
                source.Append("        auto bgcs_cstr_data = bgcs_instance->c_str();\n")
                    .Append("        if (bgcs_cstr_data == nullptr) throw std::invalid_argument(\"BGCS c_str returned a null readable pointer\");\n")
                    .Append("        uint64_t bgcs_cstr_count = 0;\n        while (bgcs_cstr_count < ").Append(configured!.cStr!.maximumCodeUnits.ToString(CultureInfo.InvariantCulture))
                    .Append("ULL && bgcs_cstr_data[static_cast<size_t>(bgcs_cstr_count)] != 0) ++bgcs_cstr_count;\n")
                    .Append("        if (bgcs_cstr_count == ").Append(configured.cStr.maximumCodeUnits.ToString(CultureInfo.InvariantCulture))
                    .Append("ULL) throw std::length_error(\"BGCS c_str has no terminator within its declared readable bound\");\n");
                if (standard.action == StandardValueAction.GetAt)
                    source.Append("        if (index >= bgcs_cstr_count) throw std::out_of_range(\"BGCS c_str index\");\n");
            }
            else if (standard.action is StandardValueAction.GetAt or StandardValueAction.SetAt or StandardValueAction.GetKeyAt or StandardValueAction.GetMappedAt)
            {
                source.Append("        if (index >= static_cast<uint64_t>(bgcs_instance->size())) throw std::out_of_range(\"BGCS collection index\");\n");
                if (standard.family is StandardValueKind.Map or StandardValueKind.Set)
                {
                    source.Append("        auto bgcs_iterator = bgcs_instance->begin();\n");
                    if (familyReader.FindBinding(module.source, export.operation.ownerType!.Value) is not null)
                        source.Append("        auto bgcs_iterator_end = bgcs_instance->end();\n        for (uint64_t bgcs_index = 0; bgcs_index < index; ++bgcs_index) {\n")
                            .Append("            if (bgcs_iterator == bgcs_iterator_end) throw std::length_error(\"BGCS configured associative count exceeds its iterator extent\");\n            ++bgcs_iterator;\n        }\n")
                            .Append("        if (bgcs_iterator == bgcs_iterator_end) throw std::length_error(\"BGCS configured associative index has no native element\");\n");
                    else source.Append("        for (uint64_t bgcs_index = 0; bgcs_index < index; ++bgcs_index) ++bgcs_iterator;\n");
                }
            }
            if (standard.action is StandardValueAction.PathUtf8Count or StandardValueAction.PathUtf8Byte
                || standard.family == StandardValueKind.Path && standard.action == StandardValueAction.CopyCodeUnits)
            {
                source.Append("        auto bgcs_path_utf8 = bgcs_instance->").Append(configured?.pathFormat == NativePathFormat.NativeUtf8 ? "u8string" : "generic_u8string")
                    .Append("();\n        if (bgcs_path_utf8.size() != 0 && bgcs_path_utf8.data() == nullptr) throw std::invalid_argument(\"BGCS nonempty path conversion requires readable data\");\n");
                if (standard.action == StandardValueAction.PathUtf8Byte)
                    source.Append("        if (index >= static_cast<uint64_t>(bgcs_path_utf8.size())) throw std::out_of_range(\"BGCS UTF-8 path byte\");\n");
            }
            if (standard.action == StandardValueAction.GetValue && standard.family is StandardValueKind.Optional or StandardValueKind.Expected)
                source.Append("        if (!bgcs_instance->has_value()) throw std::logic_error(\"BGCS optional or expected has no value\");\n");
            if (standard.action == StandardValueAction.GetAlternative && configured is not null)
                source.Append("        if (static_cast<uint64_t>(bgcs_instance->index()) != ").Append(standard.alternativeIndex!.Value.ToString(CultureInfo.InvariantCulture))
                    .Append("ULL) throw std::logic_error(\"BGCS configured variant alternative is inactive\");\n");
            if (standard.action == StandardValueAction.CopyCodeUnits)
            {
                string extent = standard.family == StandardValueKind.Path ? "bgcs_path_utf8.size()" : cStr ? "bgcs_cstr_count" : "bgcs_instance->size()";
                string element = standard.family == StandardValueKind.Path ? "bgcs_path_utf8.data()[bgcs_copy_index]" : cStr ? "bgcs_cstr_data[bgcs_copy_index]" : "(*bgcs_instance)[bgcs_copy_index]";
                string carrier = types[types[arguments[0].type].elementType!.Value].cName;
                source.Append("        const uint64_t bgcs_copy_count = static_cast<uint64_t>(").Append(extent).Append(");\n")
                    .Append("        if (bgcs_copy_count > static_cast<uint64_t>((std::numeric_limits<size_t>::max)())) throw std::length_error(\"BGCS code-unit copy exceeds native addressable extent\");\n")
                    .Append("        if (capacity < bgcs_copy_count) throw std::length_error(\"BGCS code-unit copy destination is too small\");\n")
                    .Append("        if (bgcs_copy_count != 0 && destination == nullptr) throw std::invalid_argument(\"BGCS nonempty code-unit copy requires writable storage\");\n")
                    .Append("        if (bgcs_copy_count != 0 && reinterpret_cast<uintptr_t>(destination) % alignof(").Append(carrier).Append(") != 0) throw std::invalid_argument(\"BGCS code-unit destination is not aligned for its exact C carrier\");\n")
                    .Append("        for (size_t bgcs_copy_index = 0; bgcs_copy_index < static_cast<size_t>(bgcs_copy_count); ++bgcs_copy_index) destination[bgcs_copy_index] = static_cast<")
                    .Append(carrier).Append(">(").Append(element).Append(");\n");
            }
            return standard.action switch
            {
                StandardValueAction.Construct => owner + "()",
                StandardValueAction.Count => cStr ? "bgcs_cstr_count" : "bgcs_instance->size()",
                StandardValueAction.GetAt => cStr ? "bgcs_cstr_data[static_cast<size_t>(index)]" : standard.family == StandardValueKind.Set ? "*bgcs_iterator" : "(*bgcs_instance)[static_cast<size_t>(index)]",
                StandardValueAction.SetAt => "((*bgcs_instance)[static_cast<size_t>(index)] = " + ConvertArgument(arguments[1]) + ")",
                StandardValueAction.Append => "bgcs_instance->" + (standard.family == StandardValueKind.Set ? "insert" : "push_back") + "(" + ConvertArgument(arguments[0]) + ")",
                StandardValueAction.HasValue => standard.family is StandardValueKind.UniquePointer or StandardValueKind.SharedPointer ? "static_cast<bool>(*bgcs_instance)" : "bgcs_instance->has_value()",
                StandardValueAction.GetValue => "bgcs_instance->value()",
                StandardValueAction.GetError => ExpectedError(),
                StandardValueAction.Discriminator => "bgcs_instance->index()",
                StandardValueAction.GetAlternative => configured?.variantAccess == CppVariantAccess.MemberGet
                    ? "bgcs_instance->template get<" + standard.alternativeIndex!.Value.ToString(CultureInfo.InvariantCulture) + ">()"
                    : "std::get<" + standard.alternativeIndex!.Value.ToString(CultureInfo.InvariantCulture) + ">(*bgcs_instance)",
                StandardValueAction.GetKeyAt => "bgcs_iterator->first",
                StandardValueAction.GetMappedAt => "bgcs_iterator->second",
                StandardValueAction.Insert => "bgcs_instance->insert_or_assign(" + ConvertArgument(arguments[0]) + ", " + ConvertArgument(arguments[1]) + ")",
                StandardValueAction.GetPointer => "bgcs_instance->get()",
                StandardValueAction.CloneShared => "*bgcs_instance",
                StandardValueAction.MoveUnique => "std::move(*bgcs_instance)",
                StandardValueAction.PathUtf8Count => "bgcs_path_utf8.size()",
                StandardValueAction.PathUtf8Byte => "bgcs_path_utf8.data()[static_cast<size_t>(index)]",
                StandardValueAction.DurationCount => "bgcs_instance->count()",
                StandardValueAction.TimePointCount => "bgcs_instance->time_since_epoch().count()",
                StandardValueAction.CopyCodeUnits => "bgcs_copy_count",
                _ => throw new NotSupportedException("The standard-library action has no typed native emission.")
            };

            string ExpectedError()
            {
                source.Append("        if (bgcs_instance->has_value()) throw std::logic_error(\"BGCS expected has no error\");\n");
                return "bgcs_instance->error()";
            }
        }

        string ConvertArgument(BridgeParameter parameter)
        {
            NativeType native = module.source.GetType(parameter.nativeType);
            while (native.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) native = module.source.GetType(native.elementType!.Value);
            string nativeType = NativeName(parameter.nativeType);
            if (native.category == NativeTypeCategory.Reference) nativeType = NativeName(native.elementType!.Value);
            return parameter.conversion switch
            {
                BridgeConversionKind.SourceExpression => "(" + new ControlledSourceFragmentVerifier().Substitute(parameter.conversionExpression!, new Dictionary<string, string>(StringComparer.Ordinal)
                { ["value"] = parameter.name, ["nativeType"] = nativeType }) + ")",
                BridgeConversionKind.ScalarCast => "static_cast<" + nativeType + ">(" + parameter.name + ")",
                BridgeConversionKind.PointerCast => "reinterpret_cast<" + nativeType + ">(" + parameter.name + ")",
                BridgeConversionKind.Dereference => "*reinterpret_cast<" + nativeType + "*>(" + parameter.name + ")",
                BridgeConversionKind.RecordCopy => RecordInputHelper(types[types[parameter.type].elementType!.Value]) + "(" + parameter.name + ")",
                BridgeConversionKind.Move => "std::move(*reinterpret_cast<" + nativeType + "*>(" + parameter.name + "))",
                _ => throw new NotSupportedException("The native argument has no typed conversion.")
            };
        }

        string ConvertGroup(
            BridgeExport export,
            BridgeArgumentGroup group
        ) {
            if (group.standardSequence is StandardSequenceArgument sequence)
            {
                BridgeParameter data = export.parameters.Single(parameter => parameter.id == group.inputs[0].parameter);
                BridgeParameter length = export.parameters.Single(parameter => parameter.id == group.inputs[1].parameter);
                NativeType original = BGCS.Analysis.Bridge.StandardLibraryFactReader.Resolve(module.source, group.nativeType);
                bool reference = original.category == NativeTypeCategory.Reference;
                NativeType record = reference ? BGCS.Analysis.Bridge.StandardLibraryFactReader.Resolve(module.source, original.elementType!.Value) : original;
                int groupIndex = export.operation.argumentGroups.IndexOf(group);
                string type = "bgcs_sequence_type_" + groupIndex;
                string value = "bgcs_sequence_value_" + groupIndex;
                string index = "bgcs_sequence_index_" + groupIndex;
                source.Append("        using ").Append(type).Append(" = ").Append(NativeName(record.id)).AppendLine(";")
                    .Append("        if (").Append(length.name).Append(" > static_cast<uint64_t>(std::numeric_limits<size_t>::max())) throw std::length_error(\"BGCS sequence length exceeds native size_t\");\n")
                    .Append("        if (").Append(length.name).Append(" != 0 && ").Append(data.name).Append(" == nullptr) throw std::invalid_argument(\"BGCS nonempty sequence requires data\");\n");
                if (sequence.extent is ulong extent)
                    source.Append("        if (").Append(length.name).Append(" != ").Append(extent.ToString(CultureInfo.InvariantCulture)).Append("ULL) throw std::length_error(\"BGCS sequence length differs from native fixed extent\");\n");
                if (sequence.materialization == StandardSequenceMaterialization.BorrowedView)
                {
                    source.Append("        static_assert(std::is_same_v<std::remove_cv_t<typename ").Append(type).Append("::element_type>, std::remove_cv_t<std::remove_pointer_t<decltype(")
                        .Append(data.name).Append(")>>>, \"BGCS borrowed sequence requires identical native C element storage\");\n")
                        .Append("        ").Append(type).Append(' ').Append(value).Append("(").Append(data.name).Append(", static_cast<size_t>(").Append(length.name).AppendLine("));");
                }
                else if (sequence.family == StandardValueKind.Path)
                {
                    string units = "bgcs_path_units_" + groupIndex;
                    source.Append("        if (").Append(length.name).Append(" > ").Append(StandardPathInputFacts.C_MAXIMUM_CODE_UNITS.ToString(CultureInfo.InvariantCulture))
                        .Append("ULL) throw std::length_error(\"BGCS standard path input exceeds its declared UTF-8 copy bound\");\n")
                        .Append("        std::u8string ").Append(units).AppendLine(";")
                        .Append("        ").Append(units).Append(".reserve(static_cast<size_t>(").Append(length.name).AppendLine("));")
                        .Append("        for (size_t ").Append(index).Append(" = 0; ").Append(index).Append(" < static_cast<size_t>(").Append(length.name).Append("); ++").Append(index).AppendLine(")")
                        .Append("            ").Append(units).Append(".push_back(static_cast<char8_t>(").Append(data.name).Append('[').Append(index).AppendLine("]));")
                        .Append("        ").Append(type).Append(' ').Append(value).Append('(').Append(units).AppendLine(");");
                }
                else if (new StandardLibraryFactReader(module.familyBindings).FindBinding(module.source, record.id) is { stringAccess: CppStringAccess.CStrNullTerminated, cStr: not null } cStrBinding)
                {
                    string unit = NativeName(sequence.elementType);
                    string temporary = "bgcs_cstr_units_" + groupIndex;
                    source.Append("        if (").Append(length.name).Append(" >= ").Append(cStrBinding.cStr.maximumCodeUnits.ToString(CultureInfo.InvariantCulture))
                        .Append("ULL) throw std::length_error(\"BGCS c_str input exceeds its declared terminated bound\");\n")
                        .Append("        std::vector<").Append(unit).Append("> ").Append(temporary).AppendLine(";")
                        .Append("        ").Append(temporary).Append(".reserve(static_cast<size_t>(").Append(length.name).AppendLine(") + 1);")
                        .Append("        for (size_t ").Append(index).Append(" = 0; ").Append(index).Append(" < static_cast<size_t>(").Append(length.name).Append("); ++").Append(index).AppendLine(") {")
                        .Append("            if (").Append(data.name).Append('[').Append(index).Append("] == 0) throw std::invalid_argument(\"BGCS c_str copying input rejects embedded zero code units\");\n")
                        .Append("            ").Append(temporary).Append(".push_back(static_cast<").Append(unit).Append(">(").Append(data.name).Append('[').Append(index).AppendLine("]));\n        }")
                        .Append("        ").Append(temporary).Append(".push_back(static_cast<").Append(unit).AppendLine(">(0));")
                        .Append("        ").Append(type).Append(' ').Append(value).Append('(').Append(temporary).AppendLine(".data());");
                }
                else
                {
                    source.Append("        ").Append(type).Append(' ').Append(value).AppendLine("{};");
                    if (sequence.family == StandardValueKind.Array && new StandardLibraryFactReader(module.familyBindings).FindBinding(module.source, record.id) is not null)
                        source.Append("        if (static_cast<uint64_t>(").Append(value).Append(".size()) != ").Append(sequence.extent!.Value.ToString(CultureInfo.InvariantCulture))
                            .Append("ULL) throw std::length_error(\"BGCS configured array contradicts its native fixed extent\");\n");
                    if (sequence.family is StandardValueKind.String or StandardValueKind.Vector)
                        source.Append("        ").Append(value).Append(".reserve(static_cast<size_t>(").Append(length.name).AppendLine("));");
                    source.Append("        for (size_t ").Append(index).Append(" = 0; ").Append(index).Append(" < static_cast<size_t>(").Append(length.name).Append("); ++").Append(index).AppendLine(")")
                        .Append("            ").Append(value).Append(sequence.family == StandardValueKind.Array ? "[" + index + "] = " : ".push_back(")
                        .Append("static_cast<typename ").Append(type).Append("::value_type>(").Append(data.name).Append('[').Append(index).Append("])")
                        .AppendLine(sequence.family == StandardValueKind.Array ? ";" : ");");
                }
                if (sequence.materialization == StandardSequenceMaterialization.BorrowedView
                    && new StandardLibraryFactReader(module.familyBindings).FindBinding(module.source, record.id) is not null)
                    source.Append("        if (static_cast<uint64_t>(").Append(value).Append(".size()) != ").Append(length.name)
                        .Append(" || ").Append(value).Append(".data() != ").Append(data.name)
                        .Append(") throw std::logic_error(\"BGCS configured span changed its borrowed pointer or count\");\n");
                return reference ? value : "std::move(" + value + ")";
            }
            if (group.expression is null) return ConvertArgument(export.parameters.Single(parameter => parameter.id == group.inputs[0].parameter));
            Dictionary<string, string> values = group.inputs.ToDictionary(input => input.name,
                input => export.parameters.Single(parameter => parameter.id == input.parameter).name, StringComparer.Ordinal);
            values.Add("nativeType", NativeName(group.nativeType));
            return "(" + new ControlledSourceFragmentVerifier().Substitute(group.expression, values) + ")";
        }

        ArtifactDescriptor Write(
            string path,
            string mediaType,
            string text
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            sink.WriteText(path, text);
            return new(path, mediaType, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))));
        }
    }
}
