using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Interop;

/// <summary>Checks detached native graph references and storage facts without consulting a parser.</summary>
public sealed class NativeModuleVerifier
{
    /// <summary>Checks references, pointer widths, record extents and field positions.</summary>
    /// <param name="module">The frozen native graph.</param>
    /// <returns>Errors that make the graph unsafe to analyze or emit.</returns>
    public ImmutableArray<BindingDiagnostic> Validate(NativeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        if (!Enum.IsDefined(module.byteOrder))
            Add(diagnostics, "The native graph contains an undefined byte-order fact.");
        HashSet<StableId> typeIds = module.types.Select(type => type.id).ToHashSet();
        HashSet<StableId> declarationIds = module.declarations.Select(declaration => declaration.id).ToHashSet();
        foreach (NativeType type in module.types)
        {
            CheckReference(type.elementType, typeIds, type.id, diagnostics);
            CheckReference(type.canonicalType, typeIds, type.id, diagnostics);
            CheckReference(type.returnType, typeIds, type.id, diagnostics);
            CheckReference(type.declaration, declarationIds, type.id, diagnostics);
            foreach (StableId parameter in type.parameterTypes)
                CheckReference(parameter, typeIds, type.id, diagnostics);
            if (type.size < 0 || type.alignment <= 0 || type.alignment is int alignment && (alignment & (alignment - 1)) != 0)
                Add(diagnostics, $"Native type '{type.spelling}' has an invalid known size or alignment.");
            if (type.category == NativeTypeCategory.Pointer && type.size is long size && size != module.pointerSize)
                Add(diagnostics, $"Native pointer '{type.spelling}' has {size}-byte storage on a {module.pointerSize}-byte pointer target.");
        }
        foreach (NativeDeclaration declaration in module.declarations)
        {
            NativeObjectiveCFactVerifier.Validate(module, declaration, typeIds, declarationIds, message => Add(diagnostics, message));
            if (declaration.stringConstant is { } text)
            {
                try { new NativeStringConstantVerifier().Verify(text); }
                catch (ArgumentException failure) { Add(diagnostics, $"Native string constant '{declaration.qualifiedName}' is invalid: {failure.Message}"); }
                if (declaration.constantValue is not null) Add(diagnostics, $"Native declaration '{declaration.qualifiedName}' cannot be both a scalar and a string constant.");
            }
            if (!Enum.IsDefined(declaration.referenceQualifier))
                Add(diagnostics, $"Native declaration '{declaration.qualifiedName}' has an undefined reference qualification.");
            if (!Enum.IsDefined(declaration.nativeLinkage))
                Add(diagnostics, $"Native declaration '{declaration.qualifiedName}' has an undefined native linkage.");
            if (!Enum.IsDefined(declaration.addressability))
                Add(diagnostics, $"Native declaration '{declaration.qualifiedName}' has an undefined callable addressability.");
            if (declaration.addressability != NativeCallableAddressability.Unknown
                && (declaration.kind != "Function" || declaration.addressabilityEvidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified }
                    || string.IsNullOrWhiteSpace(declaration.addressabilityEvidence.origin)))
                Add(diagnostics, $"Native declaration '{declaration.qualifiedName}' has no compiler fact provenance for its asserted callable addressability.");
            CheckReference(declaration.type, typeIds, declaration.id, diagnostics);
            CheckReference(declaration.sourceType, typeIds, declaration.id, diagnostics);
            CheckReference(declaration.returnType, typeIds, declaration.id, diagnostics);
            CheckReference(declaration.macro?.functionAlias, declarationIds, declaration.id, diagnostics);
            CheckReference(declaration.functionReference, declarationIds, declaration.id, diagnostics);
            if (declaration.functionReference is StableId referenced && declarationIds.Contains(referenced)
                && (declaration.kind != "Field" || declaration.role != "Variable" || module.GetDeclaration(referenced).kind != "Function"
                    || declaration.functionReferenceEvidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified }
                    || string.IsNullOrWhiteSpace(declaration.functionReferenceEvidence.origin)))
                Add(diagnostics, $"Native variable '{declaration.qualifiedName}' omitted its exact free-function initializer provenance.");
            if (declaration.macro?.functionAlias is StableId aliasTarget && declarationIds.Contains(aliasTarget)
                && (declaration.kind != "Macro" || declaration.macro.isFunctionLike || declaration.macro.isActive != true
                    || declaration.macro.functionAliasEvidence is not { level: EvidenceLevel.Declared or EvidenceLevel.Verified }
                    || string.IsNullOrWhiteSpace(declaration.macro.functionAliasEvidence.origin) || module.GetDeclaration(aliasTarget).kind != "Function"))
                Add(diagnostics, $"Macro alias '{declaration.qualifiedName}' has no established active object-like free-function reference.");
            CheckReference(declaration.parent, declarationIds, declaration.id, diagnostics);
            foreach (StableId child in declaration.children)
                CheckReference(child, declarationIds, declaration.id, diagnostics);
            foreach (NativeBaseClass baseClass in declaration.baseClasses)
                CheckReference(baseClass.type, typeIds, declaration.id, diagnostics);
            foreach (NativeTemplateArgument argument in declaration.templateArguments)
                CheckReference(argument.type, typeIds, declaration.id, diagnostics);
            foreach (StableId parameter in declaration.templateParameters)
                CheckReference(parameter, typeIds, declaration.id, diagnostics);
            if (declaration.offset < 0 || declaration.bitOffset < 0 || declaration.bitWidth < 0)
                Add(diagnostics, $"Native declaration '{declaration.qualifiedName}' has an invalid field position.");
            if (declaration.parent is not StableId parentId || declaration.offset is not long offset || !declarationIds.Contains(parentId))
                continue;
            NativeDeclaration parent = module.GetDeclaration(parentId);
            if (parent.type is not StableId parentType || !typeIds.Contains(parentType))
                continue;
            NativeType container = module.GetType(parentType);
            if (container.category == NativeTypeCategory.Record && container.size is long extent && offset > extent)
                Add(diagnostics, $"Field '{declaration.qualifiedName}' starts beyond its record's {extent}-byte extent.");
            if (container.category == NativeTypeCategory.Record && container.size is long recordSize
                && declaration.bitWidth is null && declaration.type is StableId fieldTypeId && typeIds.Contains(fieldTypeId)
                && module.GetType(fieldTypeId).size is long fieldSize && fieldSize > 0
                && (offset > recordSize || fieldSize > recordSize - offset))
                Add(diagnostics, $"Field '{declaration.qualifiedName}' exceeds its record's {recordSize}-byte extent.");
            if (container.category == NativeTypeCategory.Record && container.size is long bitfieldExtent
                && declaration.bitOffset is long bitOffset && declaration.bitWidth is int bitWidth
                && bitfieldExtent <= long.MaxValue / 8 && (bitOffset > bitfieldExtent * 8 || bitWidth > bitfieldExtent * 8 - bitOffset))
                Add(diagnostics, $"Bitfield '{declaration.qualifiedName}' exceeds its record's {bitfieldExtent}-byte extent.");
        }
        return diagnostics.ToImmutable();
    }

    private static void CheckReference(
        StableId? reference,
        ISet<StableId> known,
        StableId owner,
        ImmutableArray<BindingDiagnostic>.Builder diagnostics
    ) {
        if (reference is StableId id && !known.Contains(id))
            Add(diagnostics, $"Native fact '{owner}' references missing identity '{id}'.");
    }

    private static void Add(
        ImmutableArray<BindingDiagnostic>.Builder diagnostics,
        string message
    ) {
        diagnostics.Add(new(BindingDiagnosticSeverity.Error, message, "BGCS-NATIVE-GRAPH"));
    }
}
