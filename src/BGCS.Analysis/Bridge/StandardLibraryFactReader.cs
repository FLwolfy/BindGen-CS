using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Identifies standard-library specializations using detached declaration and template facts.</summary>
public sealed class StandardLibraryFactReader
{
    private readonly ImmutableArray<CppFamilyBinding> m_bindings;
    private readonly ImmutableDictionary<string, CppFamilyBinding> m_templateBindings;
    private readonly ImmutableArray<CppFamilyBinding> m_exactRequests;
    // This table belongs to this reader's frozen settings. Sharing a global module-only
    // resolved-binding cache would mix independently configured SDK projections.
    private readonly ConditionalWeakTable<NativeModule, ExactBindings> m_exactBindings = new();
    /// <summary>Freezes optional exact user-asserted family bindings in addition to known standard-library specializations.</summary>
    /// <param name="bindings">Explicit finite protocols; null selects only built-in standard families.</param>
    public StandardLibraryFactReader(IEnumerable<CppFamilyBinding>? bindings = null)
    {
        m_bindings = CppFamilyBindingValidator.Freeze(bindings?.ToImmutableArray() ?? []);
        m_templateBindings = m_bindings.Where(binding => binding.nativeTemplate is not null).ToImmutableDictionary(binding => binding.nativeTemplate!, StringComparer.Ordinal);
        m_exactRequests = m_bindings.Where(binding => binding.nativeType is not null).ToImmutableArray();
    }

    /// <summary>Gets the immutable normalized opt-in protocol collection.</summary>
    public ImmutableArray<CppFamilyBinding> bindings => m_bindings;

    /// <summary>Finds an exact configured native class-template head after ordinary alias and qualification resolution.</summary>
    /// <param name="module">The original detached graph.</param>
    /// <param name="typeId">The original type identity.</param>
    /// <returns>The explicit binding, or null when only built-in recognition applies.</returns>
    public CppFamilyBinding? FindBinding(
        NativeModule module,
        StableId typeId
    ) {
        NativeType type = Resolve(module, typeId);
        if (type.category != NativeTypeCategory.Record) return null;
        m_templateBindings.TryGetValue(BaseName(type.spelling), out CppFamilyBinding? template);
        CppFamilyBinding? exact = m_exactRequests.IsEmpty ? null : m_exactBindings.GetValue(module, ResolveExactBindings).byType.GetValueOrDefault(type.id);
        if (template is not null && exact is not null) throw new InvalidOperationException("Template and exact-type bindings select the same native record.");
        return exact ?? template;
    }

    /// <summary>Requires each requested head to match concrete compiler-derived specialization facts.</summary>
    /// <param name="module">The complete original graph.</param>
    public void ValidateBindings(NativeModule module)
    {
        foreach (CppFamilyBinding binding in m_bindings)
        {
            if (binding.nativeType is not null)
            {
                foreach (StableId exact in m_exactBindings.GetValue(module, ResolveExactBindings).byType.Where(pair => pair.Value == binding).Select(pair => pair.Key)) _ = Read(module, exact);
                continue;
            }
            NativeType[] matches = module.types.Where(type => type.category == NativeTypeCategory.Record && type.declaration is StableId declaration
                && BaseName(type.spelling) == binding.nativeTemplate && module.GetDeclaration(declaration).templateParameters.IsEmpty
                && !module.GetDeclaration(declaration).templateArguments.IsEmpty).ToArray();
            if (matches.Length == 0) throw new InvalidOperationException("Configured C++ family '" + binding.nativeTemplate + "' has no concrete compiler-derived class-template specialization; aliases of standard types already use their canonical family.");
            foreach (NativeType match in matches) _ = Read(module, match.id);
        }
    }

    private sealed record ExactBindings(ImmutableDictionary<StableId, CppFamilyBinding> byType);

    private ExactBindings ResolveExactBindings(NativeModule module)
    {
        ImmutableDictionary<StableId, CppFamilyBinding>.Builder result = ImmutableDictionary.CreateBuilder<StableId, CppFamilyBinding>();
        Dictionary<StableId, NativeDeclaration> declarations = module.declarations.ToDictionary(declaration => declaration.id);
        foreach (CppFamilyBinding binding in m_exactRequests)
        {
            NativeType[] matches = module.types.Where(type => type.category is NativeTypeCategory.Record or NativeTypeCategory.Alias
                && (CppFamilyBindingValidator.NormalizeName(type.spelling) == binding.nativeType || type.declaration is StableId declaration
                    && declarations.TryGetValue(declaration, out NativeDeclaration? named) && CppFamilyBindingValidator.NormalizeName(named.qualifiedName) == binding.nativeType))
                .Select(type => Resolve(module, type.id)).DistinctBy(type => type.id).ToArray();
            if (matches.Length != 1 || matches[0].category != NativeTypeCategory.Record || matches[0].declaration is not StableId ownerId)
                throw new InvalidOperationException("An exact configured family must resolve unambiguously to one actual native class or class alias.");
            NativeDeclaration owner = module.GetDeclaration(ownerId);
            bool standardString = binding.family == StandardValueKind.String && IsStandardString(module, matches[0], owner);
            if (!owner.isCompleteDefinition || !owner.templateParameters.IsEmpty || !owner.templateArguments.IsEmpty && !standardString)
                throw new NotSupportedException("An exact nativeType binding requires a complete non-template class or a named alias of the actual std::basic_string specialization; other concrete template families use nativeTemplate.");
            if (!result.TryAdd(matches[0].id, binding)) throw new InvalidOperationException("Distinct configured aliases cannot bind the same canonical native class more than once.");
        }
        return new(result.ToImmutable());
    }

    /// <summary>Reads an exact normalized period from compiler-evaluated ratio specialization arguments.</summary>
    /// <param name="module">The detached source graph.</param>
    /// <param name="typeId">A duration or time-point source type.</param>
    /// <returns>The established rational period, or null when the required integral facts are absent.</returns>
    public NativeDurationPeriod? ReadPeriod(
        NativeModule module,
        StableId typeId
    ) {
        StandardValueOperation? facts = Read(module, typeId);
        if (facts?.family == StandardValueKind.TimePoint) return ReadPeriod(module, facts.argumentTypes[1]);
        if (facts?.family != StandardValueKind.Duration) return null;
        return ReadDurationPeriod(module, Resolve(module, typeId));
    }

    private static NativeDurationPeriod? ReadDurationPeriod(
        NativeModule module,
        NativeType type
    ) {
        NativeDeclaration duration = module.GetDeclaration(type.declaration!.Value);
        if (duration.templateArguments.Length < 2 || duration.templateArguments[1].type is not StableId periodId) return null;
        NativeType period = Resolve(module, periodId);
        if (period.declaration is not StableId declaration || BaseName(period.spelling) != "std::ratio") return null;
        NativeDeclaration ratio = module.GetDeclaration(declaration);
        if (ratio.templateArguments.Length != 2 || !Integer(ratio.templateArguments[0], out BigInteger numerator)
            || !Integer(ratio.templateArguments[1], out BigInteger denominator) || numerator.IsZero || denominator.IsZero) return null;
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        if (numerator.Sign < 0) return null;
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= divisor;
        denominator /= divisor;
        if (numerator > long.MaxValue || denominator > long.MaxValue) return null;
        return new((long)numerator, (long)denominator, new(EvidenceLevel.Declared, "native-template-period:" + ratio.id.value));

        static bool Integer(
            NativeTemplateArgument argument,
            out BigInteger value
        ) {
            value = default;
            return argument.kind is "AsInteger" or "Integral" && argument.type is null
                && BigInteger.TryParse(argument.spelling, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>Reads the exact supported family and its explicit semantic type arguments.</summary>
    /// <param name="module">The detached source-language facts.</param>
    /// <param name="typeId">The source type, including an alias or qualified view.</param>
    /// <returns>The supported family facts, or null for an unrelated native type.</returns>
    public StandardValueOperation? Read(
        NativeModule module,
        StableId typeId
    ) {
        ArgumentNullException.ThrowIfNull(module);
        NativeType type = Resolve(module, typeId);
        if (type.category != NativeTypeCategory.Record || type.declaration is not StableId declarationId) return null;
        NativeDeclaration declaration = module.GetDeclaration(declarationId);
        string spelling = type.spelling;
        string baseName = BaseName(spelling);
        StandardValueKind? family = baseName switch
        {
            "std::basic_string" => StandardValueKind.String,
            "std::vector" => StandardValueKind.Vector,
            "std::span" => StandardValueKind.Span,
            "std::array" => StandardValueKind.Array,
            "std::map" or "std::unordered_map" => StandardValueKind.Map,
            "std::set" or "std::unordered_set" => StandardValueKind.Set,
            "std::optional" => StandardValueKind.Optional,
            "std::variant" => StandardValueKind.Variant,
            "std::expected" => StandardValueKind.Expected,
            "std::filesystem::path" or "std::filesystem::__cxx11::path" => StandardValueKind.Path,
            "std::chrono::duration" => StandardValueKind.Duration,
            "std::chrono::time_point" => StandardValueKind.TimePoint,
            "std::unique_ptr" => StandardValueKind.UniquePointer,
            "std::shared_ptr" => StandardValueKind.SharedPointer,
            _ => null
        };
        if (family is null && IsStandardString(module, type, declaration)) family = StandardValueKind.String;
        CppFamilyBinding? binding = FindBinding(module, typeId);
        if (binding is not null)
        {
            if (family is not null && family != binding.family)
                throw new InvalidOperationException("A configured family contradicts the established native standard-library family.");
            family = binding.family;
            if (!declaration.isCompleteDefinition || binding.nativeType is null && declaration.templateArguments.IsDefaultOrEmpty)
                throw new NotSupportedException("A configured family requires complete compiler-derived specialization and argument facts.");
        }
        if (family is null) return null;
        int required = family switch
        {
            StandardValueKind.Path => 0,
            StandardValueKind.Map or StandardValueKind.Expected or StandardValueKind.TimePoint => 2,
            StandardValueKind.Variant => -1,
            _ => 1
        };
        string[] argumentSpellings = SplitArguments(spelling);
        int count = required < 0 ? argumentSpellings.Length : required;
        if (count == 0 && family == StandardValueKind.Variant) throw new NotSupportedException("A variant requires concrete alternative type facts.");
        ImmutableArray<StableId>.Builder arguments = ImmutableArray.CreateBuilder<StableId>();
        StableId? exactStringUnit = null;
        if (binding?.stringAccess == CppStringAccess.CStrNullTerminated && family == StandardValueKind.String)
            exactStringUnit = ReadCStr(module, typeId).elementType;
        else if (binding?.nativeType is not null && family == StandardValueKind.String && declaration.templateArguments.IsEmpty)
        {
            NativeDeclaration[] aliases = declaration.children.Select(module.GetDeclaration).Where(child => child.name == "value_type"
                && child.kind == "Typedef" && child.visibility == "Public" && child.type is not null).ToArray();
            if (aliases.Length != 1) throw new NotSupportedException("A non-template String requires one public compiler-derived value_type alias.");
            exactStringUnit = Resolve(module, aliases[0].type!.Value).id;
        }
        for (int index = 0; index < count; index++)
        {
            StableId? argument = exactStringUnit ?? (index < declaration.templateArguments.Length ? declaration.templateArguments[index].type : null);
            if (argument is null && binding is null && index < argumentSpellings.Length)
            {
                string candidate = argumentSpellings[index];
                NativeType[] matches = module.types.Where(item => item.spelling == candidate).ToArray();
                StableId[] canonical = matches.Select(item => Resolve(module, item.id).id).Distinct().ToArray();
                if (canonical.Length == 1) argument = canonical[0];
            }
            if (argument is null) throw new NotSupportedException($"Standard-library specialization '{spelling}' has no detached type fact for argument {index}.");
            arguments.Add(argument.Value);
        }
        if (binding is not null)
        {
            if (family is StandardValueKind.Map or StandardValueKind.Set or StandardValueKind.Variant or StandardValueKind.UniquePointer or StandardValueKind.SharedPointer
                && arguments.Any(argument => Resolve(module, argument).primitiveKind == "Void"))
                throw new NotSupportedException("This configured family needs concrete nonvoid element types; an address to void does not establish a value protocol.");
            if (family == StandardValueKind.Expected && Resolve(module, arguments[1]).primitiveKind == "Void")
                throw new NotSupportedException("A configured expected error alternative requires a concrete nonvoid value; only its success alternative may be void.");
            if (family is StandardValueKind.UniquePointer or StandardValueKind.SharedPointer && Resolve(module, arguments[0]).category == NativeTypeCategory.Array)
                throw new NotSupportedException("A configured smart-owner array requires an explicit extent and buffer protocol.");
            if (family == StandardValueKind.Duration && ReadDurationPeriod(module, type) is null)
                throw new NotSupportedException("A configured duration requires concrete representation and positive, bounded std::ratio period arguments.");
            if (family == StandardValueKind.TimePoint)
            {
                NativeType durationType = Resolve(module, arguments[1]);
                if (FindBinding(module, arguments[1])?.family != StandardValueKind.Duration && BaseName(durationType.spelling) != "std::chrono::duration")
                    throw new NotSupportedException("A configured time point must name a duration family rather than a recursive or unrelated time-point argument.");
                StandardValueOperation? duration = Read(module, arguments[1]);
                if (duration?.family != StandardValueKind.Duration || ReadPeriod(module, arguments[1]) is null)
                    throw new NotSupportedException("A configured time point requires an established native duration representation and exact rational period.");
            }
            if (family is StandardValueKind.Array or StandardValueKind.Span)
                _ = StandardSequenceArgumentVerifier.ReadExtent(module, type.id, family.Value);
            if (family == StandardValueKind.String)
            {
                NativeType unit = Resolve(module, arguments[0]);
                int width = binding.stringEncoding switch { NativeTextEncoding.Utf8 => 1, NativeTextEncoding.Utf16 => 2, NativeTextEncoding.Utf32 => 4, _ => 0 };
                if (unit.category != NativeTypeCategory.Primitive || unit.size != width || unit.isSigned is null
                    || unit.primitiveKind is "Void" or "Bool" or "Float" or "Double")
                    throw new NotSupportedException("Configured string encoding contradicts the compiler-derived native code-unit storage.");
            }
        }
        return new(family.Value, StandardValueAction.Count, arguments.ToImmutable());
    }

    /// <summary>Resolves a source type without erasing its stored alias identity.</summary>
    /// <param name="module">The detached source-language facts.</param>
    /// <param name="id">The type identity to resolve.</param>
    /// <returns>The canonical nonalias, unqualified shape.</returns>
    public static NativeType Resolve(
        NativeModule module,
        StableId id
    ) {
        HashSet<StableId> visited = [];
        NativeType type = module.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
        {
            if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("The standard-library type chain is incomplete or cyclic.");
            type = module.GetType(element);
        }
        return type;
    }

    /// <summary>Reads one accessible zero-argument c_str member and its exact compiler-derived pointer element.</summary>
    /// <param name="module">The original detached type graph.</param>
    /// <param name="typeId">The actual native class or alias.</param>
    /// <returns>The chosen member identity, unqualified element identity and receiver constness.</returns>
    public CppCStrFact ReadCStr(
        NativeModule module,
        StableId typeId
    ) {
        NativeType owner = Resolve(module, typeId);
        NativeDeclaration declaration = module.GetDeclaration(owner.declaration!.Value);
        NativeDeclaration[] members = declaration.children.Select(module.GetDeclaration).Where(member => member.kind == "Function"
            && member.name == "c_str" && member.visibility == "Public" && !member.isStatic && !member.isDeleted && !member.isVolatile
            && member.hasPrototype && !member.isVariadic && member.templateParameters.IsEmpty
            && member.referenceQualifier is NativeReferenceQualifier.None or NativeReferenceQualifier.LValue
            && !member.children.Select(module.GetDeclaration).Any(child => child.kind == "Parameter") && member.returnType is not null).ToArray();
        NativeDeclaration[] preferred = members.Any(member => member.isConst) ? members.Where(member => member.isConst).ToArray() : members;
        if (preferred.Length != 1) throw new NotSupportedException("A CStr String requires one unambiguous accessible fixed zero-argument c_str member.");
        NativeDeclaration getter = preferred[0];
        NativeType pointer = Resolve(module, getter.returnType!.Value);
        if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId element)
            throw new NotSupportedException("The actual c_str result must be a native data pointer.");
        NativeType unit = Resolve(module, element);
        if (unit.category != NativeTypeCategory.Primitive || unit.size != 1 || unit.isSigned is null
            || unit.primitiveKind is "Bool" or "Void" or "Float" or "Double")
            throw new NotSupportedException("The actual c_str result must point to one-byte integral UTF-8 code-unit storage.");
        return new(getter.id, unit.id, getter.isConst);
    }

    private static bool IsStandardString(
        NativeModule module,
        NativeType type,
        NativeDeclaration declaration
    ) {
        if (BaseName(type.spelling) == "std::basic_string") return true;
        // Clang's preferred_name attribute can spell the canonical specialization
        // as std::string. Its declaration and typed template arguments still identify
        // basic_string; keep the original record identity, including its allocator.
        if (declaration.name != "basic_string" || declaration.kind != "Class" || declaration.type != type.id
            || !declaration.isCompleteDefinition || declaration.templateKind != "TemplateSpecializedClass"
            || !declaration.templateParameters.IsEmpty || declaration.templateArguments.Length != 3
            || declaration.templateArguments.Any(argument => argument.type is null)
            || declaration.parent is not StableId parentId) return false;
        NativeDeclaration parent = module.GetDeclaration(parentId);
        if (parent.kind != "Namespace") return false;
        if (parent.name is "__1" or "__cxx11")
        {
            if (parent.parent is not StableId standardId) return false;
            parent = module.GetDeclaration(standardId);
        }
        return parent.kind == "Namespace" && parent.name == "std" && parent.parent is null;
    }

    private static string BaseName(string spelling)
    {
        string name = spelling.Split('<', 2)[0].Trim().TrimStart(':');
        return CppFamilyBindingValidator.NormalizeName(name);
    }

    private static string[] SplitArguments(string spelling)
    {
        int opening = spelling.IndexOf('<');
        if (opening < 0) return [];
        List<string> arguments = [];
        int depth = 0, start = opening + 1;
        for (int index = start; index < spelling.Length; index++)
        {
            char character = spelling[index];
            if (character == '<') depth++;
            else if (character == '>')
            {
                if (depth == 0) { arguments.Add(spelling[start..index].Trim()); return arguments.ToArray(); }
                depth--;
            }
            else if (character == ',' && depth == 0) { arguments.Add(spelling[start..index].Trim()); start = index + 1; }
        }
        throw new NotSupportedException("A standard-library specialization has an incomplete native type spelling.");
    }
}
