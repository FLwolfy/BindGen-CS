using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;

namespace BGCS.NativeBuild.Planning;

/// <summary>Produces independent compiler assertions for the native storage facts consumed by generation.</summary>
public sealed class NativeStorageAssertionEmitter
{
    /// <summary>Checks primitive widths and visible named records against the actual native build compiler.</summary>
    /// <param name="module">The detached source graph used during binding analysis.</param>
    /// <param name="headers">The original absolute header inputs.</param>
    /// <param name="preamble">Explicit source input placed before original headers.</param>
    /// <param name="postamble">Explicit source input placed after original headers.</param>
    /// <returns>A C or C++ compilation unit containing no exported symbols.</returns>
    /// <exception cref="NotSupportedException">A publicly exposed native type contains a context-dependent template value that cannot be independently named from its frozen declaration evidence.</exception>
    public string Emit(
        NativeModule module,
        ImmutableArray<string> headers,
        string preamble = "",
        string postamble = ""
    ) {
        ArgumentNullException.ThrowIfNull(module);
        if (module.language is not (SourceLanguage.C or SourceLanguage.Cpp)) throw new NotSupportedException("Native storage assertions require C or C++ facts.");
        StringBuilder text = new("/* BGCS compiler verification of parsed native storage facts. */\n");
        bool intrinsicProof = module.declarations.Any(declaration => declaration.kind == "Function" && declaration.role == "Function"
            && !declaration.isSystemDeclaration && declaration.addressability == NativeCallableAddressability.DirectCallOnly);
        if (intrinsicProof)
            text.AppendLine("#if defined(_MSC_VER)\n#pragma warning(push)\n#pragma warning(error:4391 4392)\n#else\n#error BGCS direct-call-only intrinsic proof requires a supported Windows MSVC compiler profile\n#endif");
        text.Append(preamble).Append("\n#include <stddef.h>\n");
        if (module.language == SourceLanguage.Cpp) text.AppendLine("#include <type_traits>");
        foreach (string header in headers)
        {
            if (header.Any(character => character is '\r' or '\n' or '"' or '\0')) throw new ArgumentException("A native header cannot be represented as a quoted include.", nameof(headers));
            text.Append("#include \"").Append(header.Replace('\\', '/')).Append("\"\n");
        }
        text.Append(postamble).Append('\n');
        string[] commonProofTokens = ["sizeof", "unsigned", "signed", "long", "short", "int", "char", "float", "double", "void", "const", "volatile", "struct", "union", "enum",
            "__builtin_offsetof", "BGCS_NATIVE_FIELD_OFFSET"];
        foreach (string token in commonProofTokens.Concat(module.language == SourceLanguage.C
            ? new[] { "_Static_assert", "_Generic", "_Alignof", "_Bool" }
            : new[] { "static_assert", "alignof", "decltype", "static_cast", "typename", "nullptr", "bool", "std", "is_same", "is_signed", "remove_cv", "underlying_type", "type" }))
            text.Append("#if defined(").Append(token).AppendLine(")\n#error BGCS native storage proof token is shadowed by a macro\n#endif");
        // The ordinary offsetof macro belongs to the source environment and may
        // legitimately be customized by a public header. Verification must use
        // an independent compiler operation rather than that replacement.
        text.AppendLine("#if defined(_MSC_VER) && !defined(__clang__) && !defined(__cplusplus)");
        text.AppendLine("#define BGCS_NATIVE_FIELD_OFFSET(s,m) ((unsigned long long)&(((s*)0)->m))");
        text.AppendLine("#else\n#define BGCS_NATIVE_FIELD_OFFSET(s,m) __builtin_offsetof(s,m)\n#endif");
        string assertion = module.language == SourceLanguage.C ? "_Static_assert" : "static_assert";
        string alignment = module.language == SourceLanguage.C ? "_Alignof" : "alignof";
        if (!Enum.IsDefined(module.byteOrder)) throw new ArgumentException("The native source model has an unknown byte-order enumeration value.", nameof(module));
        if (module.byteOrder != NativeByteOrder.Unknown)
        {
            string expectedOrder = module.byteOrder == NativeByteOrder.LittleEndian ? "__ORDER_LITTLE_ENDIAN__" : "__ORDER_BIG_ENDIAN__";
            text.AppendLine("#if defined(__BYTE_ORDER__) && defined(__ORDER_LITTLE_ENDIAN__) && defined(__ORDER_BIG_ENDIAN__)");
            text.Append(assertion).Append("(__BYTE_ORDER__ == ").Append(expectedOrder).AppendLine(", \"BGCS native byte order differs from parsed facts\");");
            text.AppendLine("#elif defined(_MSC_VER) && (defined(_M_IX86) || defined(_M_X64) || defined(_M_AMD64) || defined(_M_ARM) || defined(_M_ARM64))");
            if (module.byteOrder == NativeByteOrder.BigEndian) text.AppendLine("#error BGCS selected MSVC architecture byte order differs from parsed facts");
            text.AppendLine("#else\n#error BGCS native compiler did not independently establish target byte order\n#endif");
        }
        Check("sizeof(void*)", module.pointerSize, "native pointer width");
        NativeCallableAssertionEmitter callableAssertions = new(module);
        StringBuilder fieldChecks = new();
        int fieldIndex = 0;
        HashSet<string> emitted = new(StringComparer.Ordinal);
        Dictionary<BGCS.Contracts.Common.StableId, bool> accessibleDeclarations = [];
        Dictionary<BGCS.Contracts.Common.StableId, bool> nameableTypes = [];
        Dictionary<BGCS.Contracts.Common.StableId, bool> visibleDeclarations = [];
        HashSet<BGCS.Contracts.Common.StableId> pendingDeclarations = [];
        HashSet<BGCS.Contracts.Common.StableId> pendingTypes = [];
        HashSet<BGCS.Contracts.Common.StableId> exposedTypes = [], exposedDeclarations = [];
        ILookup<string, NativeDeclaration> variables = module.declarations.Where(declaration => declaration.kind == "Field" && declaration.role == "Variable")
            .ToLookup(declaration => declaration.qualifiedName, StringComparer.Ordinal);
        Dictionary<string, NativeScalarConstant> valueAssertions = new(StringComparer.Ordinal);
        if (module.language == SourceLanguage.Cpp)
        {
            foreach (NativeDeclaration declaration in module.declarations.Where(declaration => !declaration.isSystemDeclaration && Visible(declaration) && ConcreteOwners(declaration)))
                ExposeDeclaration(declaration);
            foreach (BGCS.Contracts.Common.StableId identity in exposedTypes)
            {
                NativeType exposed = module.GetType(identity);
                if (exposed.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration && exposed.declaration is { } exposedId
                    && !ControlledValueOwners(module.GetDeclaration(exposedId)))
                    throw new NotSupportedException("A publicly exposed native type has a context-dependent non-type template spelling without independent declaration evidence: " + exposed.spelling);
            }
        }
        foreach (NativeType type in module.types)
        {
            if (type.size is not > 0 || type.alignment is not > 0) continue;
            string? spelling = null;
            NativeDeclaration? declaration = type.declaration is { } declarationId ? module.GetDeclaration(declarationId) : null;
            if (type.category == NativeTypeCategory.Primitive) spelling = module.language == SourceLanguage.C && type.primitiveKind == "Bool" ? "_Bool" : type.spelling;
            else if (type.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration && declaration is not null
                && declaration.name.Length != 0 && ConcreteOwners(declaration) && Accessible(declaration))
                spelling = callableAssertions.RenderStorageType(type.id, string.Empty);
            if (spelling is null || !Regex.IsMatch(spelling, "^[A-Za-z_][A-Za-z0-9_: <>*,]*$", RegexOptions.CultureInvariant) || !emitted.Add(spelling)) continue;
            Check("sizeof(" + spelling + ")", type.size.Value, "native storage size");
            Check(alignment + "(" + spelling + ")", type.alignment.Value, "native storage alignment");
            if (module.language == SourceLanguage.Cpp && declaration?.kind == "Class")
            {
                if (declaration.isStandardLayout is bool standardLayout) Check("std::is_standard_layout<" + spelling + ">::value", standardLayout ? 1 : 0, "native standard-layout trait");
                if (declaration.isTriviallyCopyable is bool triviallyCopyable) Check("std::is_trivially_copyable<" + spelling + ">::value", triviallyCopyable ? 1 : 0, "native trivial-copy trait");
                if (declaration.isDefaultConstructible is bool defaultConstructible) Check("std::is_default_constructible<" + spelling + ">::value", defaultConstructible ? 1 : 0, "native default-construction trait");
            }
            if (type.category == NativeTypeCategory.Primitive && type.isSigned is bool signed)
                Check("(((" + spelling + ")-1) < ((" + spelling + ")1))", signed ? 1 : 0, "native integral signedness");
            if (type.category == NativeTypeCategory.Enumeration && type.elementType is { } underlyingIdentity)
            {
                NativeType underlying = module.GetType(underlyingIdentity);
                HashSet<BGCS.Contracts.Common.StableId> visited = [];
                while (underlying.category == NativeTypeCategory.Alias && underlying.elementType is { } element && visited.Add(underlying.id)) underlying = module.GetType(element);
                if (underlying.category != NativeTypeCategory.Primitive || !Regex.IsMatch(underlying.spelling, "^[A-Za-z_][A-Za-z0-9_ ]*$", RegexOptions.CultureInvariant))
                    throw new NotSupportedException("The enumeration underlying type cannot be independently verified: " + spelling);
                if (module.language == SourceLanguage.Cpp)
                    text.Append("static_assert(std::is_same<typename std::underlying_type<").Append(spelling).Append(">::type, ").Append(underlying.spelling)
                        .AppendLine(">::value, \"BGCS native enum underlying type differs from parsed facts\");");
                else
                {
                    if (underlying.isSigned is not bool enumSigned) throw new NotSupportedException("The enumeration integer signedness cannot be independently verified: " + spelling);
                    Check("(((" + spelling + ")-1) < ((" + spelling + ")0))", enumSigned ? 1 : 0, "native enum integer signedness");
                    // MSVC C keeps enum types distinct in _Generic, even for ordinary
                    // signed-int enums. Check its integer representation and exact
                    // promotion type independently instead of asserting false identity.
                    text.AppendLine("#if defined(_MSC_VER) && !defined(__clang__)");
                    text.Append("_Static_assert(_Generic(+(").Append(spelling).Append(")0, ");
                    foreach (string integer in new[] { "int", "unsigned int", "long", "unsigned long", "long long", "unsigned long long" })
                        text.Append(integer).Append(": _Generic(+(").Append(underlying.spelling).Append(")0, ").Append(integer).Append(": 1, default: 0), ");
                    text.AppendLine("default: 0), \"BGCS native enum integer promotion differs from parsed facts\");");
                    text.AppendLine("#else");
                    text.Append("_Static_assert(_Generic((").Append(spelling).Append(")0, ").Append(underlying.spelling)
                        .AppendLine(": 1, default: 0), \"BGCS native enum underlying type differs from parsed facts\");");
                    text.AppendLine("#endif");
                }
            }
            if (type.category == NativeTypeCategory.Record && declaration is not null
                && (module.language == SourceLanguage.C || declaration.isStandardLayout == true))
                foreach (NativeDeclaration field in declaration.children.Select(module.GetDeclaration).Where(field => field.kind == "Field" && !field.isStatic && field.bitWidth is null
                    && (module.language == SourceLanguage.C || Accessible(field)) && field.offset is not null && Regex.IsMatch(field.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)))
                    Check("BGCS_NATIVE_FIELD_OFFSET(" + spelling + ", " + field.name + ")", field.offset!.Value, "native field offset");
            if (type.category == NativeTypeCategory.Record && declaration is not null)
                foreach (NativeDeclaration field in declaration.children.Select(module.GetDeclaration).Where(field => field.kind == "Field" && !field.isStatic && Accessible(field)
                    && Regex.IsMatch(field.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)))
                {
                    if (module.language == SourceLanguage.C && field.bitWidth is not null)
                    {
                        fieldChecks.AppendLine("/* C bitfield declarations require effective-argument fact verification; they have no addressable exact-type assertion. */");
                        continue;
                    }
                    if (field.type is not { } fieldType) throw new InvalidOperationException("A named native field omitted its exact parsed type identity.");
                    if (ContainsAnonymousType(fieldType, []) || !Nameable(fieldType))
                    {
                        NativeType storage = module.GetType(fieldType);
                        if (storage.size is > 0) Check("sizeof(((" + spelling + "*)0)->" + field.name + ")", storage.size.Value, "native anonymous field storage size");
                        fieldChecks.AppendLine("/* Unnameable field type identity is verified against effective native source facts. */");
                        continue;
                    }
                    string alias = "bgcs_verified_field_" + (fieldIndex++).ToString(CultureInfo.InvariantCulture);
                    fieldChecks.Append("typedef ").Append(callableAssertions.RenderStorageType(fieldType, module.language == SourceLanguage.C ? "(*" + alias + ")" : alias)).AppendLine(";");
                    if (module.language == SourceLanguage.Cpp)
                        fieldChecks.Append("static_assert(std::is_same<decltype(((").Append(spelling).Append("*)nullptr)->").Append(field.name).Append("), ").Append(alias)
                            .AppendLine(">::value, \"BGCS native field type differs from parsed facts\");");
                    else
                        fieldChecks.Append("_Static_assert(_Generic(&((").Append(spelling).Append("*)0)->").Append(field.name).Append(", ").Append(alias)
                            .AppendLine(": 1, default: 0), \"BGCS native field type differs from parsed facts\");");
                }
        }
        foreach (var value in valueAssertions)
        {
            Check("sizeof(" + value.Key + ")", value.Value.width, "native template constant width");
            Check("std::is_signed<typename std::remove_cv<decltype(" + value.Key + ")>::type>::value", value.Value.isSigned == true ? 1 : 0, "native template constant signedness");
            string literal = value.Value.value == "-9223372036854775808" ? "(-9223372036854775807LL - 1LL)"
                : value.Value.value + (value.Value.isSigned == true ? "LL" : "ULL");
            text.Append("static_assert((").Append(value.Key).Append(") == (").Append(literal)
                .AppendLine("), \"BGCS native template constant differs from parsed facts\");");
        }
        // Callable convention definitions precede field typedefs, including embedded callback pointer types.
        text.Append(callableAssertions.Emit()).Append(fieldChecks);
        if (intrinsicProof) text.AppendLine("#if defined(_MSC_VER)\n#pragma warning(pop)\n#endif");
        return text.ToString();

        bool ContainsAnonymousType(
            BGCS.Contracts.Common.StableId identity,
            HashSet<BGCS.Contracts.Common.StableId> visited
        ) {
            if (!visited.Add(identity)) return false;
            NativeType type = module.GetType(identity);
            if (type.category == NativeTypeCategory.Record && type.declaration is { } nativeDeclaration && module.GetDeclaration(nativeDeclaration).name.Length == 0) return true;
            return type.elementType is { } element && ContainsAnonymousType(element, visited);
        }

        bool Accessible(NativeDeclaration declaration)
        {
            if (accessibleDeclarations.TryGetValue(declaration.id, out bool accessible)) return accessible;
            if (!pendingDeclarations.Add(declaration.id)) throw new InvalidOperationException("Native storage verification encountered a cyclic declaration accessibility graph.");
            try
            {
                accessible = declaration.visibility is not ("Private" or "Protected")
                    && (declaration.parent is null || Accessible(module.GetDeclaration(declaration.parent.Value)))
                    && (module.language != SourceLanguage.Cpp || ControlledValues(declaration))
                    && declaration.templateArguments.All(argument => argument.type is not { } type || Nameable(type));
                accessibleDeclarations.Add(declaration.id, accessible);
                return accessible;
            }
            finally { pendingDeclarations.Remove(declaration.id); }
        }
        bool Nameable(BGCS.Contracts.Common.StableId identity)
        {
            if (nameableTypes.TryGetValue(identity, out bool nameable)) return nameable;
            if (!pendingTypes.Add(identity)) throw new InvalidOperationException("Native storage verification encountered a cyclic type accessibility graph.");
            try
            {
                NativeType type = module.GetType(identity);
                // Aliases are rendered through their element graph, so a
                // private alias of int does not make that frozen int unnameable.
                nameable = (type.category is not (NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
                        || type.declaration is not { } declaration || Accessible(module.GetDeclaration(declaration)))
                    && (type.elementType is not { } element || Nameable(element))
                    && (type.returnType is not { } result || Nameable(result))
                    && type.parameterTypes.All(Nameable);
                nameableTypes.Add(identity, nameable);
                return nameable;
            }
            finally { pendingTypes.Remove(identity); }
        }
        bool ConcreteOwners(NativeDeclaration declaration)
        {
            HashSet<BGCS.Contracts.Common.StableId> visited = [];
            while (true)
            {
                if (!visited.Add(declaration.id)) throw new InvalidOperationException("Native storage verification encountered a cyclic declaration owner graph.");
                if (!declaration.templateParameters.IsEmpty || declaration.templateKind is "TemplateClass" or "PartialTemplateClass" or "FunctionTemplate" or "ObjCGenericClass") return false;
                if (declaration.parent is not { } parent) return true;
                declaration = module.GetDeclaration(parent);
            }
        }
        bool Visible(NativeDeclaration declaration)
        {
            if (visibleDeclarations.TryGetValue(declaration.id, out bool visible)) return visible;
            HashSet<BGCS.Contracts.Common.StableId> owners = [];
            NativeDeclaration current = declaration;
            while (true)
            {
                if (!owners.Add(current.id)) throw new InvalidOperationException("Native storage verification encountered a cyclic declaration visibility graph.");
                if (current.visibility is "Private" or "Protected") return visibleDeclarations[declaration.id] = false;
                if (current.parent is not { } parent) return visibleDeclarations[declaration.id] = true;
                current = module.GetDeclaration(parent);
            }
        }
        void ExposeDeclaration(NativeDeclaration declaration)
        {
            if (!Visible(declaration) || !ConcreteOwners(declaration) || !exposedDeclarations.Add(declaration.id)) return;
            if (declaration.type is { } type) ExposeType(type);
            if (declaration.returnType is { } result) ExposeType(result);
            foreach (NativeDeclaration child in declaration.children.Select(module.GetDeclaration))
                if (child.kind is "Parameter" or "Field" or "Function" or "Class" or "Typedef" or "Enum") ExposeDeclaration(child);
        }
        void ExposeType(BGCS.Contracts.Common.StableId identity)
        {
            if (!exposedTypes.Add(identity)) return;
            NativeType type = module.GetType(identity);
            if (type.elementType is { } element) ExposeType(element);
            if (type.returnType is { } result) ExposeType(result);
            foreach (BGCS.Contracts.Common.StableId parameter in type.parameterTypes) ExposeType(parameter);
            if (type.category is not (NativeTypeCategory.Record or NativeTypeCategory.Enumeration) || type.declaration is not { } declared) return;
            NativeDeclaration declaration = module.GetDeclaration(declared);
            if (declaration.parent is { } parent && module.GetDeclaration(parent) is { kind: "Class", type: { } ownerType }) ExposeType(ownerType);
            foreach (NativeTemplateArgument argument in declaration.templateArguments)
                if (argument.type is { } argumentType) ExposeType(argumentType);
            foreach (NativeBaseClass baseClass in declaration.baseClasses.Where(baseClass => baseClass.visibility is not ("Private" or "Protected"))) ExposeType(baseClass.type);
            ExposeDeclaration(declaration);
        }
        bool ControlledValueOwners(NativeDeclaration declaration)
        {
            HashSet<BGCS.Contracts.Common.StableId> owners = [];
            while (true)
            {
                if (!owners.Add(declaration.id)) throw new InvalidOperationException("Native storage verification encountered a cyclic template owner graph.");
                if (!ControlledValues(declaration)) return false;
                if (declaration.parent is not { } parent) return true;
                declaration = module.GetDeclaration(parent);
            }
        }
        bool ControlledValues(NativeDeclaration declaration)
        {
            if (declaration.templateArguments.All(argument => argument.type is not null) || declaration.type is not { } identity) return true;
            string spelling = module.GetType(identity).spelling.Trim();
            if (!spelling.EndsWith('>')) return false;
            int depth = 0, opening = -1;
            for (int index = spelling.Length - 1; index >= 0; index--)
            {
                if (spelling[index] == '>') depth++;
                else if (spelling[index] == '<' && --depth == 0) { opening = index; break; }
            }
            if (opening < 0) return false;
            List<string> arguments = [];
            int start = opening + 1;
            depth = 0;
            for (int index = start; index < spelling.Length - 1; index++)
            {
                if (spelling[index] == '<') depth++;
                else if (spelling[index] == '>') depth--;
                else if (spelling[index] == ',' && depth == 0) { arguments.Add(spelling[start..index].Trim()); start = index + 1; }
            }
            arguments.Add(spelling[start..^1].Trim());
            if (arguments.Count > declaration.templateArguments.Length) return false;
            for (int index = 0; index < arguments.Count; index++)
                if (declaration.templateArguments[index].type is null
                    && !Regex.IsMatch(arguments[index], "^(?:true|false|[0-9]+[uUlLzZ]*)$", RegexOptions.CultureInvariant)
                    && !DeclaredValue(arguments[index], declaration.templateArguments[index])) return false;
            return true;
        }
        bool DeclaredValue(
            string expression,
            NativeTemplateArgument argument
        ) {
            // Never invent qualification for a context-relative SDK name. A
            // qualified reference needs an exact immutable declaration witness.
            if (!Regex.IsMatch(expression, "^(?:::)?(?:[A-Za-z_][A-Za-z0-9_]*::)+[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) return false;
            NativeDeclaration[] matches = variables[expression.StartsWith("::", StringComparison.Ordinal) ? expression[2..] : expression].ToArray();
            if (matches.Length != 1 || !Visible(matches[0]) || matches[0].constantValue is not { kind: NativeScalarConstantKind.Integer, width: > 0 and <= 8, isSigned: not null } constant
                || constant.evidence.level is not (BGCS.Contracts.Common.EvidenceLevel.Declared or BGCS.Contracts.Common.EvidenceLevel.Verified)
                || string.IsNullOrWhiteSpace(constant.evidence.origin) || matches[0].type is not { } type
                || !BigInteger.TryParse(constant.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger actual)
                || !BigInteger.TryParse(argument.spelling, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger expected) || actual != expected) return false;
            HashSet<BGCS.Contracts.Common.StableId> visited = [];
            bool immutable = false;
            while (true)
            {
                if (!visited.Add(type)) throw new InvalidOperationException("Native template constant verification encountered a cyclic type graph.");
                NativeType native = module.GetType(type);
                if (native.category == NativeTypeCategory.Qualified)
                {
                    if (native.qualifier != "Const") return false;
                    immutable = true;
                }
                else if (native.category != NativeTypeCategory.Alias)
                {
                    if (!immutable || native.category != NativeTypeCategory.Primitive || native.isSigned != constant.isSigned || native.size != constant.width) return false;
                    valueAssertions.TryAdd(expression, constant);
                    return true;
                }
                if (native.elementType is not { } element) return false;
                type = element;
            }
        }
        void Check(
            string expression,
            long expected,
            string reason
        )
            => text.Append(assertion).Append('(').Append(expression).Append(" == ").Append(expected.ToString(CultureInfo.InvariantCulture)).Append(", \"BGCS ").Append(reason).Append(" differs from parsed facts\");\n");
    }
}
