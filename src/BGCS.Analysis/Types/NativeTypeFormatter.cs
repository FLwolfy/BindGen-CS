using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Analysis.Types;

/// <summary>Formats detached source types and overload identities without accessing compiler handles.</summary>
public sealed class NativeTypeFormatter
{
    /// <summary>Formats a finite implicit default-construction identity without inventing a source declaration.</summary>
    /// <param name="module">The detached native graph.</param>
    /// <param name="owner">The exact owning record type.</param>
    /// <returns>The deterministic qualified construction selector.</returns>
    public string DefaultConstructionSignature(
        NativeModule module,
        StableId owner
    ) {
        NativeType type = BGCS.Analysis.Bridge.StandardLibraryFactReader.Resolve(module, owner);
        NativeDeclaration record = module.GetDeclaration(type.declaration ?? throw new ArgumentException("Default construction requires a declared record type.", nameof(owner)));
        if (record.name.Length == 0)
        {
            string sourceName = FormatDeclaration(module, type.id, string.Empty).TrimStart(':');
            NativeDeclaration alias = module.declarations.First(declaration => declaration.kind == "Typedef" && declaration.qualifiedName.TrimStart(':') == sourceName);
            return sourceName + "::" + alias.name + "()";
        }
        return record.qualifiedName.TrimStart(':') + "::" + record.name + "()";
    }
    /// <summary>Formats qualified source types while preserving every pointer, reference and array level.</summary>
    /// <param name="module">The detached type graph.</param>
    /// <param name="type">The type to format.</param>
    /// <param name="preserveAliases">Whether named aliases remain visible rather than using their underlying types.</param>
    /// <param name="absoluteNames">Whether source emission requires accessible concrete names and gives them a leading global scope qualifier.</param>
    /// <returns>A deterministic source-language spelling.</returns>
    public string Format(
        NativeModule module,
        StableId type,
        bool preserveAliases = true,
        bool absoluteNames = false
    )
        => Render(module, type, string.Empty, preserveAliases, absoluteNames, sourceDeclaration: absoluteNames);

    /// <summary>Inserts an identifier or callable declarator into the exact native type shape, including pointer-to-function and pointer-to-array parentheses.</summary>
    /// <param name="module">The detached native type graph and source language.</param>
    /// <param name="type">The type to declare.</param>
    /// <param name="declarator">The caller-allocated native declarator.</param>
    /// <param name="preserveAliases">Whether source typedef names remain visible.</param>
    /// <returns>A complete source-language declaration without a trailing semicolon.</returns>
    public string FormatDeclaration(
        NativeModule module,
        StableId type,
        string declarator,
        bool preserveAliases = true
    ) {
        ArgumentNullException.ThrowIfNull(declarator);
        return Render(module, type, declarator, preserveAliases, absoluteNames: false, sourceDeclaration: true);
    }

    private static string Render(
        NativeModule module,
        StableId type,
        string initialDeclarator,
        bool preserveAliases,
        bool absoluteNames,
        bool sourceDeclaration
    ) {
        ArgumentNullException.ThrowIfNull(module);
        HashSet<StableId> sourceAccessPending = [];
        return Type(type, initialDeclarator, string.Empty, []);

        static bool Identifier(string value)
        {
            bool first = true;
            foreach (Rune rune in value.EnumerateRunes())
            {
                UnicodeCategory category = Rune.GetUnicodeCategory(rune);
                bool initial = rune.Value == '_' || Rune.IsLetter(rune) || category == UnicodeCategory.LetterNumber;
                if (!(first ? initial : initial || Rune.IsDigit(rune) || category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation)) return false;
                first = false;
            }
            return !first;
        }

        bool AccessibleSource(NativeDeclaration declaration)
        {
            StableId identity = declaration.id;
            if (!sourceAccessPending.Add(identity)) return false;
            try
            {
                HashSet<StableId> owners = [];
                while (true)
                {
                    if (!owners.Add(declaration.id)) throw new InvalidOperationException("A source declaration has a cyclic owner graph.");
                    if (declaration.visibility is "Private" or "Protected" || !declaration.templateParameters.IsEmpty || declaration.kind == "Function") return false;
                    if (declaration.parent is not { } parent) return true;
                    declaration = module.GetDeclaration(parent);
                    if (module.language == BGCS.Core.Inputs.SourceLanguage.Cpp && declaration is { kind: "Class", name.Length: 0 }
                        && (declaration.type is not { } ownerType || !module.types.Any(candidate => candidate.category == NativeTypeCategory.Alias
                            && References(candidate, ownerType) && candidate.declaration is { } alias
                            && module.GetDeclaration(alias).name.Length != 0 && AccessibleSource(module.GetDeclaration(alias))))) return false;
                }
            }
            finally { sourceAccessPending.Remove(identity); }
        }

        bool References(
            NativeType candidate,
            StableId target,
            bool unqualified = false
        ) {
            HashSet<StableId> visited = [];
            while (candidate.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && candidate.elementType is { } element)
            {
                if (!visited.Add(candidate.id)) throw new InvalidOperationException("A source alias has a cyclic element graph.");
                if (unqualified && candidate.category == NativeTypeCategory.Qualified) return false;
                if (element == target) return true;
                candidate = module.GetType(element);
            }
            return false;
        }

        NativeDeclaration? SourceAlias(NativeType storage)
            => module.types.Where(candidate => candidate.category == NativeTypeCategory.Alias
                    && References(candidate, storage.id, unqualified: true) && candidate.declaration is not null)
                .Select(candidate => module.GetDeclaration(candidate.declaration!.Value))
                .Where(alias => alias.kind == "Typedef" && Identifier(alias.name) && AccessibleSource(alias))
                .OrderBy(alias => alias.qualifiedName.TrimStart(':') == storage.spelling.TrimStart(':') ? 0 : 1)
                .ThenBy(alias => alias.qualifiedName, StringComparer.Ordinal).FirstOrDefault();

        string Type(
            StableId id,
            string declarator,
            string qualifiers,
            HashSet<StableId> active
        ) {
            if (!active.Add(id)) throw new InvalidOperationException("A native type graph is cyclic while formatting its source identity.");
            NativeType native = module.GetType(id);
            try
            {
                switch (native.category)
                {
                    case NativeTypeCategory.Alias when !preserveAliases:
                        return Element(declarator, qualifiers);
                    case NativeTypeCategory.Qualified:
                        string qualification = (native.qualifier?.Contains("Const", StringComparison.Ordinal) == true ? " const" : string.Empty)
                            + (native.qualifier?.Contains("Volatile", StringComparison.Ordinal) == true ? " volatile" : string.Empty)
                            + (native.qualifier?.Contains("Restrict", StringComparison.Ordinal) == true ? " restrict" : string.Empty);
                        if (qualification.Length == 0) throw new NotSupportedException("A native type qualifier has no complete source identity.");
                        return Element(declarator, (qualifiers + qualification).Trim());
                    case NativeTypeCategory.Pointer:
                    case NativeTypeCategory.Reference:
                        string token = native.category == NativeTypeCategory.Pointer ? "*" : native.isRValueReference ? "&&" : "&";
                        string nested = (token + (qualifiers.Length == 0 ? string.Empty : " " + qualifiers) + " " + declarator).Trim();
                        NativeType pointee = module.GetType(native.elementType ?? throw new InvalidOperationException("A pointer or reference omitted its element identity."));
                        HashSet<StableId> views = [];
                        while ((pointee.category == NativeTypeCategory.Qualified || !preserveAliases && pointee.category == NativeTypeCategory.Alias)
                            && pointee.elementType is StableId underlying)
                        {
                            if (!views.Add(pointee.id)) throw new InvalidOperationException("A native declarator view is cyclic.");
                            pointee = module.GetType(underlying);
                        }
                        if (pointee.category is NativeTypeCategory.Array or NativeTypeCategory.Function or NativeTypeCategory.Block) nested = "(" + nested + ")";
                        return Element(nested, string.Empty);
                    case NativeTypeCategory.Array:
                        return Element(declarator + "[" + (native.arrayLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty) + "]", qualifiers);
                    case NativeTypeCategory.Function:
                    case NativeTypeCategory.Block:
                        if (native.returnType is not StableId result) throw new InvalidOperationException("A callable type omitted its source result identity.");
                        if (sourceDeclaration && (native.category == NativeTypeCategory.Block || native.callingConvention is not (null or "C" or "Default")))
                            throw new NotSupportedException("The native callable type requires an explicit supported calling-convention declaration.");
                        string parameters = string.Join(", ", native.parameterTypes.Select(parameter => Type(parameter, string.Empty, string.Empty, active))
                            .Concat(native.isVariadic ? ["..."] : []));
                        if (sourceDeclaration && module.language == BGCS.Core.Inputs.SourceLanguage.C && native.hasPrototype && parameters.Length == 0) parameters = "void";
                        return Type(result, declarator + "(" + parameters + ")", qualifiers, active)
                            + (native.callingConvention is null or "C" or "Default" ? string.Empty : " [" + native.callingConvention + "]");
                    case NativeTypeCategory.Record:
                    case NativeTypeCategory.Enumeration:
                    case NativeTypeCategory.Alias:
                        string name = native.category == NativeTypeCategory.Record && native.spelling.Contains('<', StringComparison.Ordinal)
                            ? native.spelling : native.declaration is StableId declaration ? module.GetDeclaration(declaration).qualifiedName : native.spelling;
                        if (sourceDeclaration && native.declaration is { } sourceIdentity)
                        {
                            if (!AccessibleSource(module.GetDeclaration(sourceIdentity)))
                                throw new NotSupportedException("The native type has no accessible concrete source declaration: " + native.spelling);
                            foreach (NativeTemplateArgument argument in module.GetDeclaration(sourceIdentity).templateArguments)
                                if (argument.type is { } argumentType) _ = Type(argumentType, string.Empty, string.Empty, active);
                        }
                        if (sourceDeclaration && module.language == BGCS.Core.Inputs.SourceLanguage.C && native.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
                        {
                            // C tags and typedefs inhabit different namespaces. A
                            // plausible compiler display string is not a witness
                            // for either namespace: use real declaration edges.
                            NativeDeclaration? tag = native.declaration is { } tagIdentity ? module.GetDeclaration(tagIdentity) : null;
                            string? prefix = native.category == NativeTypeCategory.Enumeration && tag?.kind == "Enum" ? "enum"
                                : tag is { kind: "Class", role: "Struct" } ? "struct"
                                : tag is { kind: "Class", role: "Union" } ? "union" : null;
                            if (prefix is not null && tag is not null && Identifier(tag.name)) name = prefix + " " + tag.name;
                            else
                            {
                                NativeDeclaration? alias = SourceAlias(native);
                                if (alias is null)
                                    throw new NotSupportedException("The native C storage has no compiler-established tag or typedef source declaration: '" + native.spelling
                                        + "'. Give anonymous storage an explicit source typedef or supply a typed native lowering.");
                                name = alias.name;
                            }
                        }
                        else if (sourceDeclaration && native.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration
                            && native.declaration is { } recordIdentity && module.GetDeclaration(recordIdentity).name.Length == 0)
                        {
                            // An unnamed declaration can have a real typedef, but
                            // its compiler diagnostic spelling is never a name.
                            NativeDeclaration? alias = SourceAlias(native);
                            if (alias is null)
                                throw new NotSupportedException("The native type has no nameable source declaration: " + native.spelling);
                            name = alias.qualifiedName;
                        }
                        name = absoluteNames ? "::" + name.TrimStart(':') : name.TrimStart(':');
                        return Finish(name);
                    default:
                        return Finish(sourceDeclaration && module.language == BGCS.Core.Inputs.SourceLanguage.C && native.primitiveKind == "Bool" ? "_Bool" : native.spelling);
                }
            }
            finally { active.Remove(id); }

            string Element(
                string nextDeclarator,
                string nextQualifiers
            ) => native.elementType is StableId element ? Type(element, nextDeclarator, nextQualifiers, active)
                : throw new InvalidOperationException("An element-bearing source type omitted its element identity.");
            string Finish(string name)
            {
                if (sourceDeclaration && name.IndexOfAny([';', '{', '}', '\r', '\n', '\0']) >= 0)
                    throw new NotSupportedException("The native type has no controlled source declaration: " + native.spelling);
                return (name + (qualifiers.Length == 0 ? string.Empty : " " + qualifiers)
                    + (declarator.Length == 0 ? string.Empty : " " + declarator)).Trim();
            }
        }
    }

    /// <summary>Formats an original callable identity, excluding its return type and preserving member qualifiers.</summary>
    /// <param name="module">The detached source graph.</param>
    /// <param name="callable">The original callable declaration.</param>
    /// <returns>The qualified callable name, parameter types and exact member qualification.</returns>
    public string CallableSignature(
        NativeModule module,
        NativeDeclaration callable
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(callable);
        if (callable.kind != "Function") throw new ArgumentException("An overload identity requires a native callable declaration.", nameof(callable));
        string[] parameters = callable.children.Select(module.GetDeclaration).Where(declaration => declaration.kind == "Parameter")
            .Select(parameter => Format(module, parameter.type ?? throw new InvalidOperationException("A native parameter omitted its type identity."))).ToArray();
        string qualified = callable.parent is StableId owner && module.GetDeclaration(owner) is { kind: "Class", name.Length: > 0 } record
            ? record.qualifiedName + "::" + callable.name : callable.qualifiedName;
        return qualified.TrimStart(':') + "(" + string.Join(", ", parameters.Concat(callable.isVariadic ? ["..."] : [])) + ")"
            + (callable.isConst ? " const" : string.Empty) + (callable.isVolatile ? " volatile" : string.Empty)
            + (callable.referenceQualifier switch
            {
                NativeReferenceQualifier.None => string.Empty,
                NativeReferenceQualifier.LValue => " &",
                NativeReferenceQualifier.RValue => " &&",
                _ => throw new NotSupportedException("A native member reference qualifier has no complete overload identity.")
            });
    }
}
