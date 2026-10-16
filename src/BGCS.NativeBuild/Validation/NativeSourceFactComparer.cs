using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Planning;

namespace BGCS.NativeBuild.Validation;

/// <summary>Checks frozen selected native declarations against facts extracted from actual compiler-preprocessed source.</summary>
public sealed class NativeSourceFactComparer
{
    /// <summary>Identifies declarations requiring proof beyond addressable function and field type assertions.</summary>
    /// <param name="module">The selected immutable source graph.</param>
    /// <returns>Whether concrete user declarations require an effective-source parse.</returns>
    public static bool RequiresVerification(NativeModule module)
        => module.declarations.Any(declaration => !declaration.isSystemDeclaration && (Scalar(declaration) || String(declaration) || Alias(declaration) || declaration.kind is "Class" or "Enum" or "Function"
            && declaration.templateParameters.IsEmpty && declaration.templateKind is not ("TemplateClass" or "PartialTemplateClass")
            && ConcreteAncestors(declaration, module)
            && (declaration.kind != "Class" || declaration.isCompleteDefinition)));

    /// <summary>Rejects changed constructor signatures, private or anonymous storage, bitfields and enum values.</summary>
    /// <param name="expected">The selected facts that determined the generated native and managed contract.</param>
    /// <param name="actual">The detached facts parsed from the selected compiler's preprocessed source.</param>
    /// <param name="cancellationToken">Cancels recursive type and declaration comparisons.</param>
    /// <exception cref="InvalidOperationException">An effective declaration differs or cannot be established.</exception>
    public void Verify(
        NativeModule expected,
        NativeModule actual,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.targetId != actual.targetId || expected.triple != actual.triple || expected.pointerSize != actual.pointerSize
            || expected.byteOrder != actual.byteOrder || expected.language != actual.language)
            throw new InvalidOperationException("Native effective source facts changed the resolved target, language or byte order.");
        if (actual.diagnostics.Any(diagnostic => diagnostic.isFailure))
            throw new InvalidOperationException("Native effective source fact parsing failed: " + string.Join(Environment.NewLine, actual.diagnostics.Where(diagnostic => diagnostic.isFailure)
                .Select(diagnostic => (diagnostic.location is { } location ? location.file + "(" + location.line + "," + location.column + "): " : string.Empty) + diagnostic.message)));
        HashSet<(StableId expected, StableId actual)> visitedTypes = [];
        HashSet<(StableId expected, StableId actual)> visitedDeclarations = [];
        Dictionary<string, NativeDeclaration[]> declarations = actual.declarations.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (NativeDeclaration declaration in expected.declarations.Where(declaration => !declaration.isSystemDeclaration
            && declaration.kind is "Class" or "Enum" && declaration.name.Length != 0 && Concrete(declaration) && ConcreteAncestors(declaration, expected)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declaration.kind == "Class" && !declaration.isCompleteDefinition) continue;
            if (!declarations.TryGetValue(Key(declaration), out NativeDeclaration[]? candidates)) Fail(declaration.qualifiedName, "missing declaration");
            NativeDeclaration? match = candidates!.FirstOrDefault(candidate => candidate.kind != "Class" || candidate.isCompleteDefinition);
            if (match is null) Fail(declaration.qualifiedName, "missing complete declaration");
            CompareDeclaration(declaration, match!, declaration.qualifiedName);
        }
        foreach (NativeDeclaration function in expected.declarations.Where(declaration => !declaration.isSystemDeclaration && declaration.kind == "Function"
            && !declaration.isDeleted && Concrete(declaration) && ConcreteAncestors(declaration, expected) && declaration.visibility is not ("Private" or "Protected")))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!declarations.TryGetValue(Key(function), out NativeDeclaration[]? candidates)
                || !candidates.Any(candidate => Signature(expected, function) == Signature(actual, candidate)))
                Fail(function.qualifiedName, function.role == "Constructor" ? "constructor signature" : "callable signature");
        }
        foreach (NativeDeclaration constant in expected.declarations.Where(declaration => !declaration.isSystemDeclaration && Scalar(declaration)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeDeclaration? native = constant.kind == "Macro"
                ? actual.declarations.SingleOrDefault(declaration => declaration.kind == "Field" && declaration.role == "Variable"
                    && declaration.name == NativeFactInputEmitter.MacroProbeName(constant.id))
                : declarations.TryGetValue(Key(constant), out NativeDeclaration[]? candidates) ? candidates.SingleOrDefault() : null;
            NativeScalarConstant value = constant.constantValue!;
            if (native?.constantValue is not { } effective || effective.kind != value.kind || effective.width != value.width
                || effective.isSigned != value.isSigned || effective.value != value.value)
                Fail(constant.qualifiedName, "scalar constant value, representation or availability");
        }
        foreach (NativeDeclaration constant in expected.declarations.Where(declaration => !declaration.isSystemDeclaration && String(declaration)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeStringConstant value = constant.stringConstant!;
            if (EffectiveConstant(constant, actual)?.stringConstant is not { } effective || effective.kind != value.kind
                || effective.unitWidth != value.unitWidth || !effective.codeUnits.SequenceEqual(value.codeUnits))
                Fail(constant.qualifiedName, "string literal category, exact code units or availability");
        }
        foreach (NativeDeclaration alias in expected.declarations.Where(declaration => !declaration.isSystemDeclaration && Alias(declaration)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeDeclaration? reference = EffectiveConstant(alias, actual);
            StableId? target = alias.macro?.functionAlias ?? alias.functionReference;
            if (target is null || reference?.functionReference is null)
                Fail(alias.qualifiedName, "exact function alias target availability");
            NativeDeclaration left = expected.GetDeclaration(target!.Value);
            NativeDeclaration right = actual.GetDeclaration(reference!.functionReference!.Value);
            if (Key(left) != Key(right) || Signature(expected, left) != Signature(actual, right))
                Fail(alias.qualifiedName, "exact function alias target identity or callable signature");
        }

        void CompareDeclaration(
            NativeDeclaration left,
            NativeDeclaration right,
            string path
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visitedDeclarations.Add((left.id, right.id))) return;
            if (left.kind != right.kind || left.role != right.role) Fail(path, "declaration kind");
            if (!AbiAttributes(left).SequenceEqual(AbiAttributes(right), StringComparer.Ordinal)) Fail(path, "ABI attributes");
            if (left.kind == "Class")
            {
                if (left.isAbstract != right.isAbstract || left.isCompleteDefinition != right.isCompleteDefinition) Fail(path, "record completeness or abstractness");
                if (left.isStandardLayout is bool standard && right.isStandardLayout != standard) Fail(path, "standard-layout trait");
                if (left.isTriviallyCopyable is bool trivial && right.isTriviallyCopyable != trivial) Fail(path, "trivial-copy trait");
                if (left.isDefaultConstructible is bool constructible && right.isDefaultConstructible != constructible) Fail(path, "default-construction trait");
                if (left.baseClasses.Length != right.baseClasses.Length) Fail(path, "base-class count");
                for (int index = 0; index < left.baseClasses.Length; index++)
                {
                    NativeBaseClass first = left.baseClasses[index], second = right.baseClasses[index];
                    if (first.visibility != second.visibility || first.isVirtual != second.isVirtual) Fail(path, "base-class visibility or virtualness");
                    CompareType(first.type, second.type, path + ".base[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                }
                NativeDeclaration[] fields = left.children.Select(expected.GetDeclaration).Where(child => child.kind == "Field").ToArray();
                NativeDeclaration[] nativeFields = right.children.Select(actual.GetDeclaration).Where(child => child.kind == "Field").ToArray();
                if (fields.Length != nativeFields.Length) Fail(path, "field count");
                for (int index = 0; index < fields.Length; index++)
                {
                    NativeDeclaration first = fields[index], second = nativeFields[index];
                    string fieldPath = path + "." + (first.name.Length == 0 ? "anonymous[" + index.ToString(CultureInfo.InvariantCulture) + "]" : first.name);
                    if (first.name != second.name || first.isStatic != second.isStatic || first.visibility != second.visibility
                        || first.offset != second.offset || first.bitOffset != second.bitOffset || first.bitWidth != second.bitWidth)
                        Fail(fieldPath, "field identity, position or bitfield width");
                    if (!first.type.HasValue || !second.type.HasValue) Fail(fieldPath, "missing field type");
                    CompareType(first.type!.Value, second.type!.Value, fieldPath);
                    if (!AbiAttributes(first).SequenceEqual(AbiAttributes(second), StringComparer.Ordinal)) Fail(fieldPath, "field ABI attributes");
                }
                string[] virtuals = left.children.Select(expected.GetDeclaration).Where(child => child.kind == "Function" && child.isVirtual).Select(child => Signature(expected, child)).ToArray();
                string[] nativeVirtuals = right.children.Select(actual.GetDeclaration).Where(child => child.kind == "Function" && child.isVirtual).Select(child => Signature(actual, child)).ToArray();
                if (!virtuals.SequenceEqual(nativeVirtuals, StringComparer.Ordinal)) Fail(path, "virtual member order or signature");
            }
            else if (left.kind == "Enum")
            {
                NativeDeclaration[] values = left.children.Select(expected.GetDeclaration).Where(child => child.kind == "EnumItem").ToArray();
                NativeDeclaration[] nativeValues = right.children.Select(actual.GetDeclaration).Where(child => child.kind == "EnumItem").ToArray();
                if (values.Length != nativeValues.Length) Fail(path, "enumeration member count");
                for (int index = 0; index < values.Length; index++)
                    if (values[index].name != nativeValues[index].name || values[index].evaluatedIntegralValue != nativeValues[index].evaluatedIntegralValue)
                        Fail(path + "." + values[index].name, "enumeration value");
            }
            if (left.type is StableId leftType && right.type is StableId rightType) CompareType(leftType, rightType, path);
        }

        void CompareType(
            StableId leftId,
            StableId rightId,
            string path
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            NativeType left = Canonical(expected, leftId), right = Canonical(actual, rightId);
            if (!visitedTypes.Add((left.id, right.id))) return;
            if (left.category != right.category || left.size != right.size || left.alignment != right.alignment || left.primitiveKind != right.primitiveKind
                || left.isSigned != right.isSigned || left.qualifier != right.qualifier || left.arrayLength != right.arrayLength
                || left.isRValueReference != right.isRValueReference || left.callingConvention != right.callingConvention
                || left.isVariadic != right.isVariadic || left.hasPrototype != right.hasPrototype) Fail(path, "exact native type or storage facts");
            if (left.category is NativeTypeCategory.Primitive or NativeTypeCategory.Template or NativeTypeCategory.Unexposed
                && Normalize(left.spelling) != Normalize(right.spelling)) Fail(path, "native type identity");
            if (left.elementType.HasValue != right.elementType.HasValue || left.returnType.HasValue != right.returnType.HasValue
                || left.parameterTypes.Length != right.parameterTypes.Length) Fail(path, "native type shape");
            if (left.elementType is StableId leftElement && right.elementType is StableId rightElement) CompareType(leftElement, rightElement, path + ".element");
            if (left.returnType is StableId leftReturn && right.returnType is StableId rightReturn) CompareType(leftReturn, rightReturn, path + ".return");
            for (int index = 0; index < left.parameterTypes.Length; index++) CompareType(left.parameterTypes[index], right.parameterTypes[index], path + ".parameter[" + index.ToString(CultureInfo.InvariantCulture) + "]");
            if (left.declaration is StableId leftDeclaration && right.declaration is StableId rightDeclaration)
            {
                NativeDeclaration first = expected.GetDeclaration(leftDeclaration), second = actual.GetDeclaration(rightDeclaration);
                if (first.name.Length != 0 && Normalize(first.qualifiedName) != Normalize(second.qualifiedName)) Fail(path, "named native type identity");
                if (!first.isSystemDeclaration && first.kind is "Class" or "Enum" && Concrete(first)) CompareDeclaration(first, second, path);
            }
            else if (left.declaration.HasValue != right.declaration.HasValue) Fail(path, "native type declaration identity");
        }
    }

    private static bool ConcreteAncestors(
        NativeDeclaration declaration,
        NativeModule module
    ) {
        HashSet<StableId> visited = [];
        NativeDeclaration? parent = declaration.parent is StableId owner ? module.GetDeclaration(owner) : null;
        while (parent is not null)
        {
            if (!visited.Add(parent.id)) throw new InvalidOperationException("Native effective fact verification encountered a cyclic declaration owner graph.");
            if (!Concrete(parent)) return false;
            parent = parent.parent is StableId ancestor ? module.GetDeclaration(ancestor) : null;
        }
        return true;
    }
    private static string Key(NativeDeclaration declaration) => declaration.kind + "|" + declaration.role + "|" + Normalize(declaration.qualifiedName);
    private static string Normalize(string value) => Regex.Replace(value, "\\s+", " ", RegexOptions.CultureInvariant).Trim().TrimStart(':');
    private static bool Concrete(NativeDeclaration declaration) => declaration.templateParameters.IsEmpty
        && declaration.templateKind is not ("TemplateClass" or "PartialTemplateClass" or "FunctionTemplate");
    private static bool Scalar(NativeDeclaration declaration) => declaration.constantValue is not null && declaration.visibility is not ("Private" or "Protected")
        && (declaration.kind == "Macro" && declaration.macro is { isFunctionLike: false, isActive: true }
            || declaration.kind == "Field" && declaration.role == "Variable");
    private static bool String(NativeDeclaration declaration) => declaration.stringConstant is not null && Constant(declaration);
    private static bool Alias(NativeDeclaration declaration) => (declaration.macro?.functionAlias is not null || declaration.functionReference is not null) && Constant(declaration);
    private static bool Constant(NativeDeclaration declaration) => declaration.visibility is not ("Private" or "Protected")
        && (declaration.kind == "Macro" && declaration.macro is { isFunctionLike: false, isActive: true }
            || declaration.kind == "Field" && declaration.role == "Variable");
    internal static NativeDeclaration? EffectiveConstant(
        NativeDeclaration declaration,
        NativeModule actual
    )
        => declaration.kind == "Macro" ? actual.declarations.SingleOrDefault(candidate => candidate.kind == "Field" && candidate.role == "Variable"
                && candidate.name == NativeFactInputEmitter.MacroProbeName(declaration.id))
            : actual.declarations.SingleOrDefault(candidate => Key(candidate) == Key(declaration));
    private static NativeType Canonical(
        NativeModule module,
        StableId identity
    ) {
        HashSet<StableId> visited = [];
        NativeType type = module.GetType(identity);
        while (type.category == NativeTypeCategory.Alias && type.elementType is StableId underlying)
        {
            if (!visited.Add(type.id)) throw new InvalidOperationException("A native alias cycle prevents effective fact verification.");
            type = module.GetType(underlying);
        }
        return type;
    }
    private static string Signature(
        NativeModule module,
        NativeDeclaration declaration
    ) {
        HashSet<StableId> pending = [];
        return declaration.role + "|" + declaration.name + "|" + declaration.callingConvention + "|" + declaration.isStatic + "|"
            + declaration.isConst + "|" + declaration.isVolatile + "|" + declaration.referenceQualifier + "|" + declaration.isNoThrow + "|" + declaration.isVariadic + "|"
            + declaration.hasPrototype + "|" + declaration.isDeleted + "|" + declaration.isVirtual + "|" + declaration.isAbstract + "|" + declaration.addressability + "|"
            + declaration.isDefinition + "|" + declaration.isInline + "|" + declaration.nativeLinkage + "|"
            + (declaration.returnType is StableId result ? Type(result) : "none") + "|"
            + string.Join(";", declaration.children.Select(module.GetDeclaration).Where(child => child.kind == "Parameter").Select(child =>
                (child.type is StableId type ? Type(type) : "missing") + " written:" + (child.sourceType is StableId written ? Type(written) : "unavailable")));
        string Type(StableId identity)
        {
            NativeType type = Canonical(module, identity);
            if (!pending.Add(type.id)) return "recursive";
            string name = type.declaration is StableId declarationId ? Normalize(module.GetDeclaration(declarationId).qualifiedName) : Normalize(type.spelling);
            string result = type.category + ":" + (type.category is NativeTypeCategory.Primitive or NativeTypeCategory.Record or NativeTypeCategory.Enumeration or NativeTypeCategory.Template or NativeTypeCategory.Unexposed ? name : string.Empty)
                + ":" + type.primitiveKind + ":" + type.size + ":" + type.alignment + ":" + type.isSigned + ":" + type.qualifier + ":" + type.arrayLength + ":" + type.isRValueReference
                + ":" + type.callingConvention + ":" + type.isVariadic + ":" + type.hasPrototype
                + (type.elementType is StableId element ? "<" + Type(element) + ">" : string.Empty)
                + (type.returnType is StableId returns ? "->" + Type(returns) : string.Empty) + "(" + string.Join(",", type.parameterTypes.Select(Type)) + ")";
            pending.Remove(type.id);
            return result;
        }
    }
    private static IEnumerable<string> AbiAttributes(NativeDeclaration declaration) => declaration.attributes.Where(attribute =>
        Regex.IsMatch(attribute, "packed|aligned|ms_struct|gcc_struct|transparent_union|vector_size|ext_vector_type|regparm|vectorcall|sysv_abi|ms_abi|address_space", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .Select(Normalize).Order(StringComparer.Ordinal);
    private static void Fail(
        string path,
        string difference
    ) => throw new InvalidOperationException("Native effective source facts differ from the generated contract: " + path + " (" + difference + ").");
}
