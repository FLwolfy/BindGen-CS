using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Layout;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Inputs;
using BGCS.Core.Native;

namespace BGCS.Analysis.Interop;

/// <summary>Identifies whether C declarations describe direct imports or inputs to native source lowering.</summary>
public enum CDeclarationAnalysisPurpose
{
    /// <summary>Every callable must be externally addressable without a generated source wrapper.</summary>
    DirectImports,
    /// <summary>Source-only callables may be retained for explicitly selected native bridge emission.</summary>
    NativeBridgePlanning
}

/// <summary>Builds native C ABI facts without choosing managed names, carriers or import syntax.</summary>
public sealed class CDeclarationAnalyzer
{
    /// <summary>Freezes C records, enums, callback shapes and native export signatures.</summary>
    /// <param name="module">The verified native source graph.</param>
    /// <param name="libraryId">The library's stable identity.</param>
    /// <param name="selectedFunctions">The selected free C functions; null selects all free functions.</param>
    /// <param name="purpose">Whether selected source helpers are being analyzed for native bridge planning.</param>
    /// <returns>A native-only ABI module with explicit rejection diagnostics.</returns>
    public InteropModule Analyze(
        NativeModule module,
        string libraryId,
        IEnumerable<StableId>? selectedFunctions = null,
        CDeclarationAnalysisPurpose purpose = CDeclarationAnalysisPurpose.DirectImports
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = module.diagnostics.ToBuilder();
        diagnostics.AddRange(new NativeModuleVerifier().Validate(module));
        if (module.language != SourceLanguage.C)
            diagnostics.Add(new(BindingDiagnosticSeverity.Error, "Direct C ABI analysis requires a C source graph. C++ declarations must be lowered and reparsed as a C bridge.", "BGCS-C-ABI"));
        if (diagnostics.Any(diagnostic => diagnostic.isFailure))
            return new(libraryId, module.targetId, module.pointerSize, [], [], diagnostics.ToImmutable()) { byteOrder = module.byteOrder };
        HashSet<StableId>? selection = selectedFunctions?.ToHashSet();
        BGCS.Analysis.Declarations.NativeSourceLocator sourceLocations = new(module);
        ImmutableArray<InteropFunction>.Builder functions = ImmutableArray.CreateBuilder<InteropFunction>();
        ByValueCompatibilityAnalyzer byValue = new();
        foreach (NativeDeclaration declaration in module.declarations.Where(declaration => declaration.kind == "Function"))
        {
            if (selection is not null && !selection.Contains(declaration.id))
                continue;
            if (BGCS.Analysis.Bridge.CHeaderFunctionPolicy.RequiresAdapter(declaration) && purpose != CDeclarationAnalysisPurpose.NativeBridgePlanning)
            {
                diagnostics.Add(new(selection is null ? BindingDiagnosticSeverity.Warning : BindingDiagnosticSeverity.Error,
                    $"C function '{declaration.qualifiedName}' requires a compiled header adapter. Enable source.includeHeaderHelpers and compile the emitted helper source into the configured native module, or provide an explicit native shim.", "BGCS-C-INTERNAL-LINKAGE")
                { location = declaration.source });
                continue;
            }
            if (declaration.parent is StableId parent && module.GetDeclaration(parent).kind == "Class")
                continue;
            if (declaration.returnType is not StableId returnType)
            {
                diagnostics.Add(new(BindingDiagnosticSeverity.Error, $"C export '{declaration.qualifiedName}' has no return-type fact.", "BGCS-C-ABI"));
                continue;
            }
            if (!declaration.hasPrototype)
            {
                diagnostics.Add(new(BindingDiagnosticSeverity.Error, $"C export '{declaration.qualifiedName}' has an unspecified argument list; declare an exact fixed prototype.", "BGCS-C-ABI"));
                continue;
            }
            ImmutableArray<InteropParameter> parameters = declaration.children.Select(module.GetDeclaration)
                .Where(child => child.kind == "Parameter" && child.type is not null)
                .Select(child => new InteropParameter(child.id, child.name, child.type!.Value) { source = child.source }).ToImmutableArray();
            CheckValue(returnType, $"return type of '{declaration.qualifiedName}'");
            foreach (InteropParameter parameter in parameters)
                CheckValue(parameter.type, $"parameter '{parameter.name}' of '{declaration.qualifiedName}'");
            functions.Add(new(declaration.id, declaration.name, returnType, parameters, declaration.callingConvention ?? "Unknown")
            {
                sourceDeclaration = declaration.id,
                source = declaration.source,
                sourceName = declaration.name,
                sourceQualifiedName = declaration.qualifiedName,
                sourceSignature = new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(module, declaration),
                sourceRole = declaration.role,
                isVariadic = declaration.isVariadic,
                hasPrototype = declaration.hasPrototype,
                documentation = declaration.documentation,
                documentationContent = declaration.documentationContent
            });
        }
        HashSet<StableId> written = Closure(module.declarations.Where(declaration => declaration.sourceType is not null).Select(declaration => declaration.sourceType!.Value));
        // Written arrays/functions remain complete native facts. Only their
        // adjusted pointer forms are callable ABI. Retain all other historical
        // public type roots, including independently declared aliases.
        HashSet<StableId> abi = Closure(module.types.Where(type => !written.Contains(type.id)).Select(type => type.id)
            .Concat(module.declarations.SelectMany(declaration => new[] { declaration.type, declaration.returnType }).Where(id => id is not null).Select(id => id!.Value)));
        ImmutableArray<InteropType> types = module.types.Where(type => abi.Contains(type.id)).Select(type =>
        {
            NativeDeclaration? declaration = type.declaration is StableId id ? module.GetDeclaration(id) : null;
            NativeDeclaration[] children = declaration?.children.Select(module.GetDeclaration).ToArray() ?? [];
            return new InteropType(type.id, type.spelling, type.category, type.size, type.alignment)
            {
                sourceDeclaration = type.declaration,
                source = sourceLocations.Locate(type.id),
                declarationName = declaration?.name,
                recordKind = declaration?.role,
                sourceQualifiedName = declaration?.qualifiedName,
                primitiveKind = type.primitiveKind,
                isSigned = type.isSigned,
                qualifier = type.qualifier,
                elementType = type.elementType,
                arrayLength = type.arrayLength,
                documentation = declaration?.documentation,
                returnType = type.returnType,
                parameterTypes = type.parameterTypes,
                callingConvention = type.callingConvention,
                isVariadic = type.isVariadic,
                hasPrototype = type.hasPrototype,
                fields = children.Where(child => child.kind == "Field" && !child.isStatic && child.type is not null)
                    .Select(child => new InteropField(child.id, child.name, child.type!.Value, child.offset, child.bitOffset, child.bitWidth)
                    { documentation = child.documentation }).ToImmutableArray(),
                enumValues = children.Where(child => child.kind == "EnumItem")
                    .Select(child => new InteropEnumValue(child.id, child.name, child.value, child.evaluatedIntegralValue)
                    { documentation = child.documentation }).ToImmutableArray()
            };
        }).ToImmutableArray();
        return new(libraryId, module.targetId, module.pointerSize, types, functions.ToImmutable(), diagnostics.ToImmutable()) { byteOrder = module.byteOrder };

        HashSet<StableId> Closure(IEnumerable<StableId> roots)
        {
            HashSet<StableId> visited = [];
            Stack<StableId> pending = new(roots);
            while (pending.TryPop(out StableId id))
            {
                if (!visited.Add(id)) continue;
                NativeType type = module.GetType(id);
                foreach (StableId child in new[] { type.elementType, type.canonicalType, type.returnType }.Where(child => child is not null).Select(child => child!.Value)) pending.Push(child);
                foreach (StableId parameter in type.parameterTypes) pending.Push(parameter);
            }
            return visited;
        }

        void CheckValue(
            StableId id,
            string location
        ) {
            string? restriction = byValue.GetNativeRestriction(new(module, id));
            if (restriction is not null)
                diagnostics.Add(new(BindingDiagnosticSeverity.Error, $"The {location} cannot use the direct C ABI: {restriction}", "BGCS-C-ABI")
                { location = sourceLocations.Locate(id) });
        }
    }
}
