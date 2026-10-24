using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Targets;

namespace BGCS.NativeBridge.Emission;

/// <summary>Produces independent compiler assertions for the native storage facts consumed by generation.</summary>
public sealed class NativeStorageAssertionEmitter
{
    /// <summary>Emits independent storage checks for named types and addressable anonymous members.</summary>
    /// <param name="module">The detached source graph used during binding analysis.</param>
    /// <param name="headers">The validated include spellings for the original source headers.</param>
    /// <param name="conventions">The explicitly resolved target's source and callable-assertion facts.</param>
    /// <param name="preamble">Explicit source input placed before original headers.</param>
    /// <param name="postamble">Explicit source input placed after original headers.</param>
    /// <returns>A C or C++ compilation unit containing no exported symbols.</returns>
    /// <exception cref="NotSupportedException">Selected storage has no independently expressible name or required structural proof, including context-dependent template values without frozen declaration evidence.</exception>
    public string Emit(
        NativeModule module,
        ImmutableArray<string> headers,
        NativeSourceConventions conventions,
        string preamble = "",
        string postamble = ""
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(conventions);
        NativeCallableAssertionFacts callableFacts = conventions.callableAssertions
            ?? throw new InvalidOperationException("Native callable assertions require explicit target-owned convention facts.");
        if (callableFacts.defaultCConvention is not ("C" or "Win64" or "X86_64SysV"))
            throw new NotSupportedException("The target's default C calling convention has no implemented exact assertion: " + callableFacts.defaultCConvention);
        if (module.language is not (SourceLanguage.C or SourceLanguage.Cpp)) throw new NotSupportedException("Native storage assertions require C or C++ facts.");
        StringBuilder text = new("/* BGCS compiler verification of parsed native storage facts. */\n");
        bool intrinsicProof = module.declarations.Any(declaration => declaration.kind == "Function" && declaration.role == "Function"
            && !declaration.isSystemDeclaration && declaration.addressability == NativeCallableAddressability.DirectCallOnly);
        if (intrinsicProof)
        {
            if (!callableFacts.permitsDirectCallOnlyVoidIntrinsic)
                throw new NotSupportedException("The target does not permit a compiler-diagnostic proof for direct-call-only native intrinsics.");
            text.AppendLine("#if defined(_MSC_VER)\n#pragma warning(push)\n#pragma warning(error:4391 4392)\n#else\n#error BGCS direct-call-only intrinsic proof requires the declared compiler diagnostics\n#endif");
        }
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
            : new[] { "static_assert", "alignof", "decltype", "static_cast", "typename", "using", "nullptr", "bool", "std", "is_same", "is_const", "is_volatile", "is_signed", "is_lvalue_reference", "is_rvalue_reference", "remove_cv", "underlying_type", "type" }))
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
        NativeCallableAssertionEmitter callableAssertions = new(module, callableFacts);
        StringBuilder fieldChecks = new();
        int fieldIndex = 0;
        HashSet<string> emitted = new(StringComparer.Ordinal);
        Dictionary<BGCS.Core.Common.StableId, bool> accessibleDeclarations = [];
        Dictionary<BGCS.Core.Common.StableId, bool> nameableTypes = [];
        Dictionary<BGCS.Core.Common.StableId, bool> visibleDeclarations = [];
        HashSet<BGCS.Core.Common.StableId> pendingDeclarations = [];
        HashSet<BGCS.Core.Common.StableId> pendingTypes = [];
        HashSet<BGCS.Core.Common.StableId> exposedTypes = [], exposedDeclarations = [];
        ILookup<string, NativeDeclaration> variables = module.declarations.Where(declaration => declaration.kind == "Field" && declaration.role == "Variable")
            .ToLookup(declaration => declaration.qualifiedName, StringComparer.Ordinal);
        Dictionary<string, NativeScalarConstant> valueAssertions = new(StringComparer.Ordinal);
        if (module.language == SourceLanguage.Cpp)
        {
            foreach (NativeDeclaration declaration in module.declarations.Where(declaration => !declaration.isSystemDeclaration && Visible(declaration) && ConcreteOwners(declaration)))
                ExposeDeclaration(declaration);
            foreach (BGCS.Core.Common.StableId identity in exposedTypes)
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
                && ConcreteOwners(declaration) && Accessible(declaration) && Nameable(type.id))
                spelling = callableAssertions.RenderStorageType(type.id, string.Empty);
            if (spelling is null || !emitted.Add(spelling)) continue;
            string offsetOwner = spelling;
            if (module.language == SourceLanguage.Cpp && type.category == NativeTypeCategory.Record && spelling.Contains(',', StringComparison.Ordinal))
            {
                // A preprocessor macro cannot take a comma-containing template
                // type as one argument. A source alias preserves its identity.
                offsetOwner = "bgcs_verified_storage_" + (fieldIndex++).ToString(CultureInfo.InvariantCulture);
                text.Append("using ").Append(offsetOwner).Append(" = ").Append(spelling).AppendLine(";");
            }
            Check("sizeof(" + spelling + ")", type.size.Value, "native storage size");
            Check(alignment + "(" + spelling + ")", type.alignment.Value, "native storage alignment");
            if (module.language == SourceLanguage.Cpp && declaration?.kind == "Class")
            {
                // Only a positive fact admits DTO storage and C++ field-offset
                // use. A negative parser observation keeps those routes closed;
                // an opaque native owner need not also be non-standard-layout.
                if (declaration.isStandardLayout == true) Check("std::is_standard_layout<" + spelling + ">::value", 1, "native standard-layout trait");
                if (declaration.isTriviallyCopyable is bool triviallyCopyable) Check("std::is_trivially_copyable<" + spelling + ">::value", triviallyCopyable ? 1 : 0, "native trivial-copy trait");
                if (declaration.isDefaultConstructible is bool defaultConstructible) Check("std::is_default_constructible<" + spelling + ">::value", defaultConstructible ? 1 : 0, "native default-construction trait");
            }
            if (type.category == NativeTypeCategory.Primitive && type.isSigned is bool signed)
                Check("(((" + spelling + ")-1) < ((" + spelling + ")1))", signed ? 1 : 0, "native integral signedness");
            if (type.category == NativeTypeCategory.Enumeration && type.elementType is { } underlyingIdentity)
            {
                NativeType underlying = module.GetType(underlyingIdentity);
                HashSet<BGCS.Core.Common.StableId> visited = [];
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
                    && (module.language == SourceLanguage.C || Accessible(field)) && field.offset is not null && MemberIdentifier(field.name)))
                    Check("BGCS_NATIVE_FIELD_OFFSET(" + offsetOwner + ", " + field.name + ")", field.offset!.Value, "native field offset");
            if (type.category == NativeTypeCategory.Record && declaration is not null)
                foreach (NativeDeclaration field in declaration.children.Select(module.GetDeclaration).Where(field => field.kind == "Field" && !field.isStatic && Accessible(field)
                    && (field.name.Length == 0 || MemberIdentifier(field.name))))
                {
                    if (field.name.Length == 0)
                    {
                        if (field.type is not { } anonymousType || field.offset is not { } anonymousOffset)
                            throw new NotSupportedException("Anonymous storage omitted its compiler-established field type or offset.");
                        VerifyStructuralMembers(FieldShape(anonymousType), offsetOwner, string.Empty, anonymousOffset, [],
                            module.language == SourceLanguage.C || declaration.isStandardLayout == true);
                        continue;
                    }
                    if (module.language == SourceLanguage.C && field.bitWidth is not null)
                    {
                        fieldChecks.AppendLine("/* C bitfields have no addressable exact-type assertion; parsed bit offsets and widths remain part of the binding contract. */");
                        continue;
                    }
                    if (field.type is not { } fieldType) throw new InvalidOperationException("A named native field omitted its exact parsed type identity.");
                    NativeType fieldShape = FieldShape(fieldType);
                    if (module.language == SourceLanguage.Cpp && fieldShape.category == NativeTypeCategory.Reference)
                    {
                        if (field.storageSize is not > 0 || field.storageEvidence is not { level: BGCS.Core.Common.EvidenceLevel.Declared or BGCS.Core.Common.EvidenceLevel.Verified }
                            || string.IsNullOrWhiteSpace(field.storageEvidence.origin))
                            throw new NotSupportedException("A native reference field omitted its compiler-established slot size: " + field.qualifiedName);
                        // sizeof(field) measures the referent. A single-field carrier
                        // independently checks the storage representation of the reference.
                        string carrier = "bgcs_reference_field_slot_" + (fieldIndex++).ToString(CultureInfo.InvariantCulture);
                        string expression = "decltype(((" + spelling + "*)nullptr)->" + field.name + ")";
                        // Name the type first: some compilers otherwise parse a
                        // function-reference decltype member as a member function.
                        fieldChecks.Append("using ").Append(carrier).Append("_type = ").Append(expression).AppendLine(";");
                        fieldChecks.Append("struct ").Append(carrier).Append(" { ").Append(carrier).AppendLine("_type slot; };");
                        fieldChecks.Append("static_assert(sizeof(").Append(carrier).Append(") == ").Append(field.storageSize.Value.ToString(CultureInfo.InvariantCulture))
                            .AppendLine(", \"BGCS native reference field storage differs from parsed facts\");");
                        fieldChecks.Append("static_assert(std::").Append(fieldShape.isRValueReference ? "is_rvalue_reference<" : "is_lvalue_reference<")
                            .Append(expression).AppendLine(">::value, \"BGCS native reference field category differs from parsed facts\");");
                    }
                    if (!Nameable(fieldType))
                    {
                        if (field.offset is not { } offset)
                            throw new NotSupportedException("Structural member storage omitted its compiler-established offset: " + field.qualifiedName);
                        fieldChecks.AppendLine("/* Unnameable storage is verified structurally through its owner, without a fabricated nominal identity. */");
                        if (module.language == SourceLanguage.Cpp && declaration.isStandardLayout != true)
                            fieldChecks.AppendLine("/* Opaque non-standard-layout C++ owners retain member size/type checks; standard-layout offsets are not asserted. */");
                        VerifyMember(fieldType, offsetOwner, field.name, offset, [], module.language == SourceLanguage.C || declaration.isStandardLayout == true);
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

        NativeType FieldShape(BGCS.Core.Common.StableId identity)
        {
            NativeType type = module.GetType(identity);
            HashSet<BGCS.Core.Common.StableId> visited = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is { } element)
            {
                if (!visited.Add(type.id)) throw new InvalidOperationException("Native field storage verification encountered a cyclic type graph.");
                type = module.GetType(element);
            }
            return type;
        }

        static bool MemberIdentifier(string name)
        {
            bool first = true;
            foreach (Rune rune in name.EnumerateRunes())
            {
                UnicodeCategory category = Rune.GetUnicodeCategory(rune);
                bool initial = rune.Value == '_' || Rune.IsLetter(rune) || category == UnicodeCategory.LetterNumber;
                if (!(first ? initial : initial || Rune.IsDigit(rune) || category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation)) return false;
                first = false;
            }
            return !first;
        }

        void VerifyMember(
            BGCS.Core.Common.StableId identity,
            string owner,
            string path,
            long offset,
            HashSet<BGCS.Core.Common.StableId> active,
            bool checkOffsets,
            string inheritedQualifiers = ""
        ) {
            NativeType storage = module.GetType(identity);
            string qualifiers = StructuralQualifiers(identity, inheritedQualifiers);
            if (storage.size is not > 0) throw new NotSupportedException("A structural native field has no complete storage size: " + path);
            string expression = "((" + owner + "*)0)->" + path;
            Check("sizeof(" + expression + ")", storage.size.Value, "native structural field size");
            if (checkOffsets) CheckStructuralOffset(owner, path, offset);
            if (Nameable(identity))
            {
                string alias = "bgcs_verified_field_" + (fieldIndex++).ToString(CultureInfo.InvariantCulture);
                fieldChecks.Append("typedef ").Append(callableAssertions.RenderStorageType(identity, alias)).AppendLine(";");
                if (module.language == SourceLanguage.C)
                    fieldChecks.Append("_Static_assert(_Generic(&(").Append(expression).Append("), ").Append(qualifiers).Append(alias)
                        .AppendLine("*: 1, default: 0), \"BGCS native structural field type differs from parsed facts\");");
                else
                    fieldChecks.Append("static_assert(std::is_same<decltype(").Append(expression).Append("), ").Append(alias)
                        .AppendLine(">::value, \"BGCS native structural field type differs from parsed facts\");");
                return;
            }
            if (module.language == SourceLanguage.Cpp)
            {
                string ownQualifiers = StructuralQualifiers(identity, string.Empty);
                // decltype of a member access preserves its declared cv-qualification;
                // comparing the observed flags to frozen booleans is not a nominal
                // comparison of a compiler-derived type with itself.
                Check("std::is_const<decltype(" + expression + ")>::value", ownQualifiers.Contains("const", StringComparison.Ordinal) ? 1 : 0, "native structural const qualifier");
                Check("std::is_volatile<decltype(" + expression + ")>::value", ownQualifiers.Contains("volatile", StringComparison.Ordinal) ? 1 : 0, "native structural volatile qualifier");
            }
            NativeType shape = FieldShape(identity);
            if (shape.category == NativeTypeCategory.Array && shape.elementType is { } element && shape.arrayLength is > 0)
            {
                if (!active.Add(shape.id)) throw new InvalidOperationException("A structural native array has a cyclic element graph.");
                Check("sizeof(" + expression + ") / sizeof((" + expression + ")[0])", shape.arrayLength.Value, "native structural array extent");
                try { VerifyMember(element, owner, path + "[0]", offset, active, checkOffsets, qualifiers); }
                finally { active.Remove(shape.id); }
                return;
            }
            if (shape.category != NativeTypeCategory.Record)
                throw new NotSupportedException("A native member has no independent source type or structural proof: " + path);
            VerifyStructuralMembers(shape, owner, path, offset, active, checkOffsets, qualifiers);
        }

        void CheckStructuralOffset(
            string owner,
            string path,
            long offset
        ) {
            string direct = "BGCS_NATIVE_FIELD_OFFSET(" + owner + ", " + path + ")";
            if (module.language != SourceLanguage.C || !path.Contains("[0]", StringComparison.Ordinal))
            {
                Check(direct, offset, "native structural field offset");
                return;
            }
            // MSVC's C constant evaluator cannot evaluate array member-designators
            // in offsetof. Its documented C11/C17 __typeof__ extension (VS17.9+)
            // names the observed element only to express relative offsets. Each
            // offset still compares against independent, frozen numeric facts.
            text.AppendLine("#if defined(_MSC_VER) && !defined(__clang__)\n#if _MSC_VER < 1939\n#error BGCS structural array offsets require MSVC 17.9 or newer __typeof__ support\n#endif\n#if defined(__typeof__)\n#error BGCS structural array proof token is shadowed by a macro\n#endif");
            Check(RelativeOffset(owner, path), offset, "native structural field offset");
            text.AppendLine("#else");
            Check(direct, offset, "native structural field offset");
            text.AppendLine("#endif");

            string RelativeOffset(
                string record,
                string member
            ) {
                int index = member.IndexOf("[0]", StringComparison.Ordinal);
                if (index < 0) return "BGCS_NATIVE_FIELD_OFFSET(" + record + ", " + member + ")";
                string prefix = member[..index];
                int end = index;
                while (member.AsSpan(end).StartsWith("[0]", StringComparison.Ordinal)) end += 3;
                string firstElementOffset = "BGCS_NATIVE_FIELD_OFFSET(" + record + ", " + prefix + ")";
                if (end == member.Length) return firstElementOffset;
                string alias = "bgcs_structural_element_" + (fieldIndex++).ToString(CultureInfo.InvariantCulture);
                text.Append("typedef __typeof__(((").Append(record).Append("*)0)->").Append(member[..end]).Append(") ").Append(alias).AppendLine(";");
                return "(" + firstElementOffset + " + " + RelativeOffset(alias, member[(end + 1)..]) + ")";
            }
        }

        string StructuralQualifiers(
            BGCS.Core.Common.StableId identity,
            string inherited
        ) {
            NativeType type = module.GetType(identity);
            HashSet<BGCS.Core.Common.StableId> visited = [];
            bool constant = inherited.Contains("const", StringComparison.Ordinal), vol = inherited.Contains("volatile", StringComparison.Ordinal);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Array)
            {
                if (!visited.Add(type.id)) throw new InvalidOperationException("Structural member qualification has a cyclic element graph.");
                constant |= type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true;
                vol |= type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Volatile", StringComparison.Ordinal) == true;
                if (type.elementType is not { } element) break;
                type = module.GetType(element);
            }
            return (constant ? "const " : string.Empty) + (vol ? "volatile " : string.Empty);
        }

        void VerifyStructuralMembers(
            NativeType storage,
            string owner,
            string path,
            long offset,
            HashSet<BGCS.Core.Common.StableId> active,
            bool checkOffsets,
            string inheritedQualifiers = ""
        ) {
            if (storage.category != NativeTypeCategory.Record || storage.declaration is not { } identity
                || !module.GetDeclaration(identity).isCompleteDefinition)
                throw new NotSupportedException("Structural native storage requires a complete record declaration.");
            if (!active.Add(storage.id)) throw new InvalidOperationException("Anonymous native storage has a cyclic by-value graph.");
            try
            {
                NativeDeclaration[] members = module.GetDeclaration(identity).children.Select(module.GetDeclaration)
                    .Where(member => member.kind == "Field" && !member.isStatic).ToArray();
                if (members.Length == 0) throw new NotSupportedException("Anonymous native storage omitted its field graph.");
                foreach (NativeDeclaration member in members)
                {
                    // A public member expression can be legal even when the
                    // containing type cannot itself be named (C++ private type).
                    if (member.visibility is "Private" or "Protected" || member.bitWidth is not null)
                        throw new NotSupportedException("A structural anonymous member has no addressable public field proof: " + member.qualifiedName);
                    if (member.type is not { } memberType || member.offset is not { } memberOffset)
                        throw new NotSupportedException("An anonymous member omitted its compiler-established type or offset.");
                    long absoluteOffset = checked(offset + memberOffset);
                    if (member.name.Length == 0)
                        VerifyStructuralMembers(FieldShape(memberType), owner, path, absoluteOffset, active, checkOffsets, StructuralQualifiers(memberType, inheritedQualifiers));
                    else
                    {
                        if (!MemberIdentifier(member.name))
                            throw new NotSupportedException("A structural field has no usable source member name: " + member.qualifiedName);
                        VerifyMember(memberType, owner, path.Length == 0 ? member.name : path + "." + member.name, absoluteOffset, active, checkOffsets, inheritedQualifiers);
                    }
                }
            }
            finally { active.Remove(storage.id); }
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
        bool Nameable(BGCS.Core.Common.StableId identity)
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
                if (nameable && type.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
                {
                    try { callableAssertions.RenderStorageType(identity, string.Empty); }
                    catch (NotSupportedException) { nameable = false; }
                }
                nameableTypes.Add(identity, nameable);
                return nameable;
            }
            finally { pendingTypes.Remove(identity); }
        }
        bool ConcreteOwners(NativeDeclaration declaration)
        {
            HashSet<BGCS.Core.Common.StableId> visited = [];
            while (true)
            {
                if (!visited.Add(declaration.id)) throw new InvalidOperationException("Native storage verification encountered a cyclic declaration owner graph.");
                if (!declaration.templateParameters.IsEmpty || declaration.templateKind is "TemplateClass" or "PartialTemplateClass" or "FunctionTemplate") return false;
                if (declaration.parent is not { } parent) return true;
                declaration = module.GetDeclaration(parent);
            }
        }
        bool Visible(NativeDeclaration declaration)
        {
            if (visibleDeclarations.TryGetValue(declaration.id, out bool visible)) return visible;
            HashSet<BGCS.Core.Common.StableId> owners = [];
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
        void ExposeType(BGCS.Core.Common.StableId identity)
        {
            if (!exposedTypes.Add(identity)) return;
            NativeType type = module.GetType(identity);
            if (type.elementType is { } element) ExposeType(element);
            if (type.returnType is { } result) ExposeType(result);
            foreach (BGCS.Core.Common.StableId parameter in type.parameterTypes) ExposeType(parameter);
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
            HashSet<BGCS.Core.Common.StableId> owners = [];
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
                || constant.evidence.level is not (BGCS.Core.Common.EvidenceLevel.Declared or BGCS.Core.Common.EvidenceLevel.Verified)
                || string.IsNullOrWhiteSpace(constant.evidence.origin) || matches[0].type is not { } type
                || !BigInteger.TryParse(constant.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger actual)
                || !BigInteger.TryParse(argument.spelling, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger expected) || actual != expected) return false;
            HashSet<BGCS.Core.Common.StableId> visited = [];
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
