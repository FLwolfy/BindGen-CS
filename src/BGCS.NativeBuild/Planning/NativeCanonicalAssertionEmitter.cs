using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;

namespace BGCS.NativeBuild.Planning;

/// <summary>Emits supplementary selected-native C++ proofs for aliases, variables, enumeration values and scalar constants.</summary>
/// <remarks>Emitting this source does not execute or certify it. Storage, callable, bridge and effective-source verification remain independently required.</remarks>
public sealed class NativeCanonicalAssertionEmitter
{
    /// <summary>Preserves original include-time state and freezes selected expressions before adding proof support headers.</summary>
    /// <param name="module">The selected detached C++ facts, including reachable SDK types.</param>
    /// <param name="headers">Original header paths, in their original order.</param>
    /// <param name="preamble">Original source input before the headers.</param>
    /// <param name="postamble">Original source input after the headers.</param>
    /// <returns>A complete supplementary C++ compilation unit; floating-point bit proofs require native C++20 bit-cast support.</returns>
    /// <exception cref="NotSupportedException">An exposed fact has no implemented independent canonical proof, including a public user bitfield or special scalar.</exception>
    /// <exception cref="InvalidOperationException">The detached graph contains a cycle or omits a required type identity.</exception>
    public string Emit(
        NativeModule module,
        ImmutableArray<string> headers,
        string preamble = "",
        string postamble = ""
    ) {
        ArgumentNullException.ThrowIfNull(module);
        if (module.language != SourceLanguage.Cpp) throw new NotSupportedException("Independent canonical assertions require C++ source facts.");
        if (headers.IsDefaultOrEmpty) throw new ArgumentException("Canonical assertions require the original header inputs.", nameof(headers));
        Dictionary<StableId, NativeType> types = module.types.ToDictionary(type => type.id);
        Dictionary<StableId, NativeDeclaration> declarations = module.declarations.ToDictionary(declaration => declaration.id);
        HashSet<StableId> completedTypes = [], pendingTypes = [], visitedDeclarations = [], pendingOwners = [];
        Dictionary<StableId, bool> accessible = [];
        HashSet<string> guards = new(StringComparer.Ordinal)
        {
            "constexpr", "auto", "typedef", "static_assert", "sizeof", "decltype", "static_cast", "typename", "template", "class", "struct", "namespace",
            "std", "type", "value", "true", "false", "bool", "void", "char", "short", "int", "long", "signed", "unsigned", "float", "double", "const", "volatile",
            "is_same", "is_enum", "is_integral", "is_signed", "remove_cv", "underlying_type", "bit_cast", "uint32_t", "uint64_t",
            "T", "bgcs_canonical_integer_type_impl"
        };
        StringBuilder snapshots = new(), checks = new();
        NativeCallableAssertionEmitter renderer = new(module);
        bool requiresCallableSupport = false;
        int index = 0;
        foreach (NativeDeclaration declaration in module.declarations.Where(declaration => !declaration.isSystemDeclaration && Accessible(declaration) && Concrete(declaration)))
            VisitDeclaration(declaration);

        StringBuilder source = new("/* BGCS supplementary native canonical proofs; compilation is required. */\n");
        source.Append(preamble).Append('\n');
        foreach (string header in headers)
        {
            if (header.Any(character => character is '\r' or '\n' or '"' or '\0')) throw new ArgumentException("A canonical proof header cannot be represented as a quoted include.", nameof(headers));
            source.Append("#include \"").Append(header.Replace('\\', '/')).AppendLine("\"");
        }
        source.Append(postamble).Append('\n');
        Guard();
        source.Append(snapshots);
        // Header-defined macro values are already frozen. Support headers must
        // neither supply their value nor change the original include-time state.
        source.AppendLine("#include <type_traits>\n#include <bit>\n#include <cstdint>");
        Guard();
        source.AppendLine("template<class T, bool = std::is_enum<T>::value> struct bgcs_canonical_integer_type_impl { typedef T type; };")
            .AppendLine("template<class T> struct bgcs_canonical_integer_type_impl<T, true> { typedef typename std::underlying_type<T>::type type; };");
        if (requiresCallableSupport) source.Append(renderer.Emit());
        source.Append(checks);
        return source.ToString();

        void VisitDeclaration(NativeDeclaration declaration)
        {
            if (!visitedDeclarations.Add(declaration.id) || !Concrete(declaration)) return;
            if (declaration.kind == "Field" && declaration.bitWidth is not null && !declaration.isSystemDeclaration && Accessible(declaration))
                throw new NotSupportedException("A selected C++ bitfield has no independent native layout proof: " + declaration.qualifiedName);
            if (declaration.type is StableId type) VisitType(type);
            if (declaration.returnType is StableId result) VisitType(result);
            if (declaration.kind == "Field" && declaration.role == "Variable" && Accessible(declaration))
            {
                if (declaration.type is not StableId variableType) throw new InvalidOperationException("A selected variable omitted its canonical type: " + declaration.qualifiedName);
                Identity("decltype(" + Name(declaration.qualifiedName) + ")", variableType, "variable type");
            }
            if (declaration.constantValue is { } constant && Accessible(declaration)) Constant(declaration, constant);
            if (declaration.kind == "Function")
                foreach (NativeDeclaration parameter in declaration.children.Select(Declaration).Where(child => child.kind == "Parameter")) VisitDeclaration(parameter);
        }

        void VisitType(StableId identity)
        {
            if (completedTypes.Contains(identity)) return;
            if (!pendingTypes.Add(identity)) throw new InvalidOperationException("A cyclic native type graph prevents canonical verification.");
            NativeType type = Type(identity);
            try
            {
                if (type.category == NativeTypeCategory.Primitive)
                {
                    string expected = type.primitiveKind switch
                    {
                        "Void" => "void",
                        "Bool" => "bool",
                        "WChar" => "wchar_t",
                        "Char8" => "char8_t",
                        "Char16" => "char16_t",
                        "Char32" => "char32_t",
                        "Char" when type.spelling == "signed char" => "signed char",
                        "Char" => "char",
                        "UnsignedChar" => "unsigned char",
                        "Short" => "short",
                        "UnsignedShort" => "unsigned short",
                        "Int" => "int",
                        "UnsignedInt" => "unsigned int",
                        "Long" => "long",
                        "UnsignedLong" => "unsigned long",
                        "LongLong" => "long long",
                        "UnsignedLongLong" => "unsigned long long",
                        "Float" => "float",
                        "Double" => "double",
                        "LongDouble" => "long double",
                        _ => throw new NotSupportedException("The selected primitive has no independent canonical proof: " + type.spelling)
                    };
                    if (type.spelling != expected) throw new NotSupportedException("A canonical primitive spelling differs from its frozen category: " + type.spelling);
                }
                else if (type.category is NativeTypeCategory.Template or NativeTypeCategory.Unexposed or NativeTypeCategory.Block)
                    throw new NotSupportedException("The selected type has no independent canonical proof: " + type.spelling);
                if (type.category == NativeTypeCategory.Record)
                {
                    // A record can legitimately contain a pointer to itself.
                    // Only that declaration edge closes recursive object graphs;
                    // alias and declarator cycles above remain invalid.
                    completedTypes.Add(identity);
                    if (type.declaration is not StableId record) throw new InvalidOperationException("A canonical record omitted its declaration.");
                    NativeDeclaration owner = Declaration(record);
                    foreach (NativeTemplateArgument argument in owner.templateArguments)
                        if (argument.type is StableId argumentType) VisitType(argumentType);
                    foreach (NativeBaseClass parent in owner.baseClasses.Where(parent => parent.visibility is not ("Private" or "Protected"))) VisitType(parent.type);
                    foreach (NativeDeclaration child in owner.children.Select(Declaration))
                    {
                        // Opaque SDK implementation fields are not a managed
                        // layout contract. Keep their effective facts intact,
                        // but do not name or recursively project private state.
                        if (!Accessible(child)) continue;
                        VisitDeclaration(child);
                    }
                }
                if (type.elementType is StableId element) VisitType(element);
                if (type.returnType is StableId returned) VisitType(returned);
                foreach (StableId parameter in type.parameterTypes) VisitType(parameter);
                if (type.category == NativeTypeCategory.Alias && type.declaration is StableId aliasId)
                {
                    NativeDeclaration alias = Declaration(aliasId);
                    if (Accessible(alias) && Concrete(alias)) Identity(Name(alias.qualifiedName), identity, "alias canonical type");
                }
                if (type.category == NativeTypeCategory.Enumeration && type.declaration is StableId enumId)
                {
                    NativeDeclaration enumeration = Declaration(enumId);
                    if (Accessible(enumeration) && Concrete(enumeration))
                        foreach (NativeDeclaration item in enumeration.children.Select(Declaration).Where(child => child.kind == "EnumItem"))
                        {
                            string snapshot = Snapshot(Name(item.qualifiedName));
                            if (type.elementType is not StableId underlying) throw new InvalidOperationException("An enumeration omitted its underlying type.");
                            Identity("typename std::underlying_type<typename std::remove_cv<decltype(" + snapshot + ")>::type>::type", underlying, "enum underlying type");
                            IntegerValue(snapshot, item.evaluatedIntegralValue ?? throw new InvalidOperationException("An enumeration omitted its native value."), "enumeration value");
                        }
                }
                completedTypes.Add(identity);
            }
            finally { pendingTypes.Remove(identity); }
        }

        void Identity(
            string actual,
            StableId expected,
            string reason
        ) {
            RequireNameable(expected, []);
            string alias = Generated("type");
            string rendered = renderer.RenderStorageType(expected, alias);
            requiresCallableSupport |= rendered.Contains("BGCS_VERIFY_CC_", StringComparison.Ordinal);
            Tokens(rendered);
            checks.Append("typedef ").Append(rendered).AppendLine(";");
            Check("std::is_same<" + actual + ", " + alias + ">::value", reason);
        }

        void Constant(
            NativeDeclaration declaration,
            NativeScalarConstant constant
        ) {
            string expression;
            if (declaration.kind == "Macro")
            {
                if (declaration.macro is not { isFunctionLike: false, isActive: true } || !Regex.IsMatch(declaration.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                    throw new NotSupportedException("A selected scalar macro has no exact active object-like definition.");
                expression = declaration.name;
                snapshots.Append("#if !defined(").Append(expression).AppendLine(")\n#error BGCS selected canonical macro is unavailable\n#endif");
            }
            else expression = Name(declaration.qualifiedName);
            string snapshot = Snapshot(expression), actual = "typename std::remove_cv<decltype(" + snapshot + ")>::type";
            Check("sizeof(" + snapshot + ") == " + constant.width.ToString(CultureInfo.InvariantCulture), "scalar constant width");
            switch (constant.kind)
            {
                case NativeScalarConstantKind.Integer:
                    if (constant.width is not (1 or 2 or 4 or 8) || constant.isSigned is not bool signed) throw new NotSupportedException("A scalar integer constant omitted its exact supported representation.");
                    string integer = Generated("integer");
                    checks.Append("typedef typename bgcs_canonical_integer_type_impl<").Append(actual).Append(">::type ").Append(integer).AppendLine(";");
                    Check("std::is_integral<" + integer + ">::value && !std::is_same<" + integer + ", bool>::value && std::is_signed<" + integer + ">::value == " + (signed ? "true" : "false"), "scalar constant integer type");
                    BigInteger number = ParseInteger(constant.value);
                    int bits = constant.width * 8;
                    BigInteger minimum = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
                    BigInteger maximum = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
                    if (number < minimum || number > maximum) throw new InvalidOperationException("A scalar constant exceeds its frozen integer representation.");
                    IntegerValue(snapshot, constant.value, "scalar constant value");
                    break;
                case NativeScalarConstantKind.Boolean:
                    if (constant.value is not ("0" or "1")) throw new InvalidOperationException("A Boolean constant requires zero or one.");
                    Check("std::is_same<" + actual + ", bool>::value", "scalar constant Boolean type");
                    Check(snapshot + " == " + (constant.value == "1" ? "true" : "false"), "scalar constant value");
                    break;
                case NativeScalarConstantKind.Binary32:
                case NativeScalarConstantKind.Binary64:
                    int width = constant.kind == NativeScalarConstantKind.Binary32 ? 4 : 8;
                    if (constant.width != width || !Regex.IsMatch(constant.value, width == 4 ? "^[0-9A-F]{8}$" : "^[0-9A-F]{16}$", RegexOptions.CultureInvariant))
                        throw new InvalidOperationException("A floating constant omitted its exact IEEE bits.");
                    Check("std::is_same<" + actual + ", " + (width == 4 ? "float" : "double") + ">::value", "scalar constant floating type");
                    Check("std::bit_cast<std::uint" + (width * 8).ToString(CultureInfo.InvariantCulture) + "_t>(" + snapshot + ") == 0x" + constant.value + "ULL", "scalar constant IEEE bits");
                    break;
                default: throw new NotSupportedException("The selected constant has no independent canonical proof.");
            }
        }

        void IntegerValue(
            string actual,
            string value,
            string reason
        ) {
            BigInteger number = ParseInteger(value);
            if (number < long.MinValue || number > ulong.MaxValue) throw new NotSupportedException("The selected native value exceeds the independent integer proof range.");
            string literal = number == long.MinValue ? "(-9223372036854775807LL - 1LL)" : number.ToString(CultureInfo.InvariantCulture) + (number.Sign < 0 ? "LL" : "ULL");
            Check("static_cast<" + (number.Sign < 0 ? "long long" : "unsigned long long") + ">(" + actual + ") == " + literal, reason);
        }

        void RequireNameable(
            StableId identity,
            HashSet<StableId> pending
        ) {
            if (!pending.Add(identity)) throw new InvalidOperationException("A cyclic canonical declarator cannot be rendered.");
            NativeType type = Type(identity);
            if (type.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
            {
                NativeDeclaration declaration = Declaration(type.declaration ?? throw new InvalidOperationException("A named canonical type omitted its declaration."));
                if (declaration.name.Length == 0 || !Accessible(declaration)) throw new NotSupportedException("The expected canonical type cannot be independently named: " + type.spelling);
                foreach (NativeTemplateArgument argument in declaration.templateArguments)
                    if (argument.type is StableId argumentType) RequireNameable(argumentType, pending);
            }
            if (type.elementType is StableId element) RequireNameable(element, pending);
            if (type.returnType is StableId result) RequireNameable(result, pending);
            foreach (StableId parameter in type.parameterTypes) RequireNameable(parameter, pending);
            pending.Remove(identity);
        }

        bool Accessible(NativeDeclaration declaration)
        {
            if (accessible.TryGetValue(declaration.id, out bool result)) return result;
            if (!pendingOwners.Add(declaration.id)) throw new InvalidOperationException("A cyclic canonical declaration owner graph is invalid.");
            try
            {
                result = declaration.visibility is not ("Private" or "Protected") && (declaration.parent is not StableId parent || Accessible(Declaration(parent)));
                accessible.Add(declaration.id, result);
                return result;
            }
            finally { pendingOwners.Remove(declaration.id); }
        }
        bool Concrete(NativeDeclaration declaration)
        {
            HashSet<StableId> owners = [];
            while (true)
            {
                if (!owners.Add(declaration.id)) throw new InvalidOperationException("A cyclic canonical declaration owner graph is invalid.");
                if (!declaration.templateParameters.IsEmpty || declaration.templateKind is "TemplateClass" or "PartialTemplateClass" or "FunctionTemplate") return false;
                if (declaration.parent is not StableId parent) return true;
                declaration = Declaration(parent);
            }
        }
        string Name(string name)
        {
            if (!Regex.IsMatch(name, "^(::)?[A-Za-z_][A-Za-z0-9_: <>*,&-]*$", RegexOptions.CultureInvariant))
                throw new NotSupportedException("A selected native identity has no controlled canonical expression: " + name);
            Tokens(name);
            return name.StartsWith("::", StringComparison.Ordinal) ? name : "::" + name;
        }
        string Generated(string suffix)
        {
            string name = "bgcs_canonical_" + suffix + "_" + (index++).ToString(CultureInfo.InvariantCulture);
            guards.Add(name);
            return name;
        }
        string Snapshot(string expression)
        {
            string name = Generated("snapshot");
            snapshots.Append("constexpr auto ").Append(name).Append(" = (").Append(expression).AppendLine(");");
            return name;
        }
        void Check(
            string expression,
            string reason
        ) => checks.Append("static_assert(").Append(expression).Append(", \"BGCS native canonical ").Append(reason).AppendLine(" differs from parsed facts\");");
        void Tokens(string text)
        {
            foreach (Match token in Regex.Matches(text, "[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)) guards.Add(token.Value);
        }
        void Guard()
        {
            foreach (string token in guards.Order(StringComparer.Ordinal))
                source.Append("#if defined(").Append(token).AppendLine(")\n#error BGCS canonical proof token is shadowed by a macro\n#endif");
        }
        NativeType Type(StableId identity) => types.TryGetValue(identity, out NativeType? type) ? type : throw new InvalidOperationException("A canonical proof references a missing type: " + identity.value);
        NativeDeclaration Declaration(StableId identity) => declarations.TryGetValue(identity, out NativeDeclaration? declaration) ? declaration : throw new InvalidOperationException("A canonical proof references a missing declaration: " + identity.value);
        BigInteger ParseInteger(string value) => BigInteger.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger number)
            ? number : throw new InvalidOperationException("A canonical native integer value is malformed.");
    }
}
