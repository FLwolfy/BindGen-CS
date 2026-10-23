using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Analysis.Types;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;

namespace BGCS.NativeBridge.Planning;

/// <summary>Materializes explicitly requested fixed native signatures; the native compiler performs all ellipsis promotions.</summary>
public sealed class NativeVariadicAdapterPlanner
{
    private static readonly FrozenSet<string> s_keywords = ("alignas alignof and and_eq asm auto bitand bitor bool break case catch char char8_t char16_t char32_t class compl concept const consteval constexpr constinit const_cast continue co_await co_return co_yield decltype default delete do double dynamic_cast else enum explicit export extern false float for friend goto if inline int long mutable namespace new noexcept not not_eq nullptr operator or or_eq private protected public register reinterpret_cast requires return short signed sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union unsigned using virtual void volatile wchar_t while xor xor_eq _Alignas _Alignof _Atomic _Bool _Complex _Generic _Imaginary _Noreturn _Static_assert _Thread_local").Split(' ').ToFrozenSet(StringComparer.Ordinal);
    /// <summary>Checks finite request identities and native declaration component syntax before a generation session starts.</summary>
    /// <param name="requests">The complete finite variant requests.</param>
    public void ValidateRequests(ImmutableArray<VariadicVariantRequest> requests)
    {
        if (requests.IsDefault) throw new ArgumentException("Variadic variant requests must be an initialized immutable collection.", nameof(requests));
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (VariadicVariantRequest request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.function);
            Identifier(request.name);
            if (!names.Add(request.name)) throw new ArgumentException("Finite variadic adapter names must be unique.", nameof(requests));
            if (request.parameters.IsDefault) throw new ArgumentException("Finite variadic parameter lists must be initialized.", nameof(requests));
            HashSet<string> parameters = new(StringComparer.Ordinal);
            foreach (VariadicArgumentRequest parameter in request.parameters)
            {
                ArgumentNullException.ThrowIfNull(parameter);
                Identifier(parameter.name);
                if (!parameters.Add(parameter.name)) throw new ArgumentException("Finite additional native argument names must be unique.", nameof(requests));
                if (string.IsNullOrWhiteSpace(parameter.nativeType) || parameter.nativeType.Length > 4096
                    || !Regex.IsMatch(parameter.nativeType, "^[A-Za-z_][A-Za-z0-9_: *\\t]*$", RegexOptions.CultureInvariant))
                    throw new ArgumentException("A finite variadic native type must be a native scalar, enum, pointer or typedef declaration component; use an explicit native shim for other declaration syntax.", nameof(requests));
            }
        }
    }

    /// <summary>Declares finite extra argument type queries in the first and only original-header parse.</summary>
    /// <param name="requests">The explicitly configured finite variants.</param>
    /// <param name="language">The original source language.</param>
    /// <returns>Target-parsed typedefs, without callable bodies or invented native facts.</returns>
    public string TypeProbePostamble(
        ImmutableArray<VariadicVariantRequest> requests,
        SourceLanguage language
    ) {
        ValidateRequests(requests);
        if (language is not (SourceLanguage.C or SourceLanguage.Cpp)) throw new ArgumentException("Finite type queries require C or C++.", nameof(language));
        StringBuilder result = new();
        if (!requests.IsEmpty && language == SourceLanguage.Cpp) result.AppendLine("namespace BGCSGeneratedVariadicTypes {");
        foreach (VariadicVariantRequest request in requests)
            for (int index = 0; index < request.parameters.Length; index++)
                result.Append("typedef ").Append(request.parameters[index].nativeType).Append(' ').Append(ProbeName(request.name, index)).AppendLine(";");
        if (!requests.IsEmpty && language == SourceLanguage.Cpp) result.AppendLine("}");
        return result.ToString();
    }

    /// <summary>Creates native fixed wrappers from the exact original callable facts without projecting any ellipsis into C#.</summary>
    /// <param name="module">The original detached native declarations.</param>
    /// <param name="requests">The explicitly selected finite variants.</param>
    /// <returns>Typed mappings using real first-parse argument types and content-identified native wrapper source.</returns>
    public NativeVariadicAdapterPlan Plan(
        NativeModule module,
        ImmutableArray<VariadicVariantRequest> requests
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ValidateRequests(requests);
        if (module.language is not (SourceLanguage.C or SourceLanguage.Cpp)) throw new ArgumentException("Finite variadic adapters require a C or C++ source graph.", nameof(module));
        NativeTypeFormatter formatter = new();
        ImmutableArray<NativeVariadicAdapter>.Builder adapters = ImmutableArray.CreateBuilder<NativeVariadicAdapter>();
        HashSet<StableId> dependencies = [];
        HashSet<StableId> types = [];
        StringBuilder source = new();
        bool cpp = module.language == SourceLanguage.Cpp;
        if (!requests.IsEmpty && cpp) source.AppendLine("namespace BGCSGeneratedVariadics {");
        foreach (VariadicVariantRequest request in requests)
        {
            NativeDeclaration[] candidates = module.declarations.Where(declaration => declaration.kind == "Function" && Matches(declaration, request.function)).ToArray();
            if (candidates.Length != 1) throw new ArgumentException($"Finite variadic selector '{request.function}' must resolve to exactly one original callable.", nameof(requests));
            NativeDeclaration function = candidates[0];
            if (module.declarations.Any(declaration => declaration.kind == "Macro" && declaration.name == function.name && declaration.macro?.isActive != false))
                throw new NotSupportedException("A finite variadic target is shadowed by an active source macro; provide an explicit native shim that fixes the intended callable identity.");
            if (!function.isVariadic || !function.hasPrototype || function.returnType is null || function.isDeleted || function.templateParameters.Length != 0
                || function.visibility is "Private" or "Protected" || function.addressability == NativeCallableAddressability.DirectCallOnly
                || function.parent is StableId parent && module.GetDeclaration(parent).kind == "Class"
                || function.callingConvention is not (null or "C" or "Default")
                || (function.isStatic || function.nativeLinkage == NativeLinkageKind.Internal) && !function.isDefinition)
                throw new NotSupportedException($"Finite variadic adapter '{request.name}' requires an accessible, concrete free variadic function with an exact C/default native prototype.");
            string wrapper = cpp ? "BGCSGeneratedVariadics::" + request.name : request.name;
            if (module.declarations.Any(declaration => declaration.qualifiedName == wrapper)
                || module.declarations.Any(declaration => declaration.kind == "Macro" && declaration.name == request.name && declaration.macro?.isActive != false))
                throw new ArgumentException($"Finite variadic adapter '{wrapper}' conflicts with an existing native declaration or active macro.", nameof(requests));
            NativeDeclaration[] prefix = function.children.Select(module.GetDeclaration).Where(declaration => declaration.kind == "Parameter").ToArray();
            HashSet<string> parameterNames = request.parameters.Select(parameter => parameter.name).ToHashSet(StringComparer.Ordinal);
            List<string> declarations = [], arguments = [];
            for (int index = 0; index < prefix.Length; index++)
            {
                NativeDeclaration parameter = prefix[index];
                StableId type = parameter.type ?? throw new InvalidOperationException("A native variadic fixed parameter omitted its adjusted native type.");
                string name = parameter.name;
                if (!IsIdentifier(name) || !parameterNames.Add(name))
                {
                    name = "bgcs_fixed_" + index.ToString(CultureInfo.InvariantCulture);
                    while (!parameterNames.Add(name)) name += "_argument";
                }
                declarations.Add(formatter.FormatDeclaration(module, type, name));
                arguments.Add(cpp ? "static_cast<decltype(" + name + ")&&>(" + name + ")" : name);
                types.Add(type);
            }
            ImmutableArray<StableId>.Builder argumentTypes = ImmutableArray.CreateBuilder<StableId>();
            for (int index = 0; index < request.parameters.Length; index++)
            {
                VariadicArgumentRequest parameter = request.parameters[index];
                string probe = (cpp ? "BGCSGeneratedVariadicTypes::" : "") + ProbeName(request.name, index);
                NativeDeclaration query = module.declarations.SingleOrDefault(declaration => declaration.kind == "Typedef" && declaration.qualifiedName == probe)
                    ?? throw new InvalidOperationException("The original frontend parse omitted finite argument type query '" + probe + "'.");
                StableId typeId = query.type ?? throw new InvalidOperationException("A finite type query omitted its actual native type identity.");
                NativeType type = Resolve(module, typeId);
                if (type.category is not (NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration or NativeTypeCategory.Pointer)
                    || type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Void")
                    throw new NotSupportedException("Finite variadic arguments require actual scalar, enum or pointer type facts; other forms require an explicit shim.");
                argumentTypes.Add(typeId); types.Add(typeId);
                declarations.Add(parameter.nativeType + " " + parameter.name);
                arguments.Add(parameter.name);
            }
            string parameters = declarations.Count == 0 && !cpp ? "void" : string.Join(", ", declarations);
            source.Append("static inline ").Append(formatter.FormatDeclaration(module, function.returnType.Value, request.name + "(" + parameters + ")")).AppendLine(" {");
            string signatureName = Local("bgcs_variadic_signature"), targetName = Local("bgcs_variadic_target");
            string originalParameters = string.Join(", ", prefix.Select(parameter => formatter.FormatDeclaration(module, parameter.type!.Value, string.Empty)).Append("..."));
            source.Append("    typedef ").Append(formatter.FormatDeclaration(module, function.returnType.Value,
                "(*" + signatureName + ")(" + originalParameters + ")")).AppendLine(";");
            source.Append("    ").Append(signatureName).Append(' ').Append(targetName).Append(" = ")
                .Append(cpp ? "static_cast<" + signatureName + ">(&::" + function.qualifiedName.TrimStart(':') + ")" : "&" + function.name).AppendLine(";");
            NativeType result = Resolve(module, function.returnType.Value);
            source.Append("    ").Append(result.primitiveKind == "Void" ? string.Empty : "return ")
                .Append(targetName).Append('(').Append(string.Join(", ", arguments)).AppendLine(");\n}");
            dependencies.Add(function.id);
            types.Add(function.returnType.Value);
            adapters.Add(new(function.id, formatter.CallableSignature(module, function), wrapper, prefix.Length, request.parameters) { argumentTypes = argumentTypes.ToImmutable() });

            string Local(string name)
            {
                while (!parameterNames.Add(name)) name += "_local";
                return name;
            }
        }
        if (!requests.IsEmpty && cpp) source.AppendLine("}");
        string text = source.ToString();
        return new(new("bgcs.builtin.finite-variadic", SourceFragmentKind.TranslationUnit, text,
            new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))))
        {
            declarationDependencies = dependencies.OrderBy(id => id.value, StringComparer.Ordinal).ToImmutableArray(),
            typeDependencies = types.OrderBy(id => id.value, StringComparer.Ordinal).ToImmutableArray(),
            evidence = new(EvidenceLevel.UserAsserted, "source.variadicVariants:explicit-native-types")
        }, adapters.ToImmutable());

        bool Matches(
            NativeDeclaration declaration,
            string selector
        ) {
            if (declaration.id.value == selector || declaration.qualifiedName.TrimStart(':') == selector.TrimStart(':')) return true;
            try { return formatter.CallableSignature(module, declaration) == selector.TrimStart(':'); }
            catch (NotSupportedException) { return false; }
        }
    }

    private static string ProbeName(
        string variantName,
        int index
    ) {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(variantName)))[..16];
        return "BGCSFiniteType_" + hash + "_" + index.ToString(CultureInfo.InvariantCulture);
    }

    private static NativeType Resolve(
        NativeModule module,
        StableId id
    ) {
        NativeType type = module.GetType(id);
        HashSet<StableId> seen = [];
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!seen.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A finite variadic argument has an incomplete or cyclic native type view.");
            type = module.GetType(element);
        }
        return type;
    }

    private static void Identifier(string name)
    {
        if (!IsIdentifier(name)) throw new ArgumentException("Finite variadic adapter and argument names must be unreserved native identifiers.");
    }

    private static bool IsIdentifier(string name) => name is not null && Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
        && !name.Contains("__", StringComparison.Ordinal)
        && !s_keywords.Contains(name);
}
