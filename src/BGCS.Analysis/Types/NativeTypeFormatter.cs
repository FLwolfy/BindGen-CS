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
        return record.qualifiedName.TrimStart(':') + "::" + record.name + "()";
    }
    /// <summary>Formats qualified source types while preserving every pointer, reference and array level.</summary>
    /// <param name="module">The detached type graph.</param>
    /// <param name="type">The type to format.</param>
    /// <param name="preserveAliases">Whether named aliases remain visible rather than using their underlying types.</param>
    /// <param name="absoluteNames">Whether named types receive a leading global scope qualifier for source emission.</param>
    /// <returns>A deterministic source-language spelling.</returns>
    public string Format(
        NativeModule module,
        StableId type,
        bool preserveAliases = true,
        bool absoluteNames = false
    )
        => Render(module, type, string.Empty, preserveAliases, absoluteNames, sourceDeclaration: false);

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
                        if (sourceDeclaration && module.language == BGCS.Core.Inputs.SourceLanguage.C && native.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
                        {
                            // Compiler spelling distinguishes real tag names from typedef names for anonymous storage.
                            // The display declaration name cannot establish which C namespace owns that identity.
                            string[] words = native.spelling.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            bool identifier = words.Length == 1 && Identifier(words[0]);
                            bool tag = words.Length == 2 && words[0] is "struct" or "union" or "enum" && Identifier(words[1]);
                            if (!identifier && !tag)
                                throw new NotSupportedException("The native C storage has no usable compiler-established declaration spelling: '" + native.spelling
                                    + "'. Give anonymous storage an explicit source typedef or supply a typed native lowering.");
                            name = string.Join(" ", words);
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
            string Finish(string name) => (name + (qualifiers.Length == 0 ? string.Empty : " " + qualifiers)
                + (declarator.Length == 0 ? string.Empty : " " + declarator)).Trim();
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
        string qualified = callable.parent is StableId owner && module.GetDeclaration(owner) is { kind: "Class" } record
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
