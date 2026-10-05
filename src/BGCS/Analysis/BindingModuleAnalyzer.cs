using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Analysis.Constants;
using BGCS.Configuration;
using BGCS.Conversion;
using BGCS.Core.Collections;
using BGCS.Core.Text;
using BGCS.CppAst.Extensions;

namespace BGCS.Analysis;

using System.Diagnostics.CodeAnalysis;
using BGCS.Configuration.Mapping;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CSharp;
using BGCS.Intermediate;

/// <summary>
/// Lowers a declaration dependency graph into the shared binding intermediate representation.
/// </summary>
public sealed class BindingModuleAnalyzer
{
    private readonly CsCodeGeneratorConfig m_config;
    private readonly TypeAnalyzer m_typeAnalyzer;
    private readonly AbiLayoutAnalyzer m_layoutAnalyzer;
    private readonly OwnershipAnalyzer m_ownershipAnalyzer;
    private readonly OverloadPlanner m_overloadPlanner;

    /// <summary>
    /// Creates the analysis pipeline for one configured generation attempt.
    /// </summary>
    /// <param name="config">Target and emission configuration borrowed for the duration of analysis.</param>
    public BindingModuleAnalyzer(CsCodeGeneratorConfig config)
    {
        this.m_config = config ?? throw new ArgumentNullException(nameof(config));
        this.m_typeAnalyzer = new(config);
        this.m_layoutAnalyzer = new(config, this.m_typeAnalyzer);
        this.m_ownershipAnalyzer = new(config);
        this.m_overloadPlanner = new();
    }

    /// <summary>
    /// Freezes the selected declaration closure and optional constants into a binding module.
    /// </summary>
    /// <param name="graph">Dependency graph whose source compilation remains alive during analysis.</param>
    /// <param name="macros">Optional macro declarations selected for constant analysis.</param>
    /// <param name="aliases">Optional attempt-local aliases discovered during preprocessing; configured aliases override matching discovered names.</param>
    /// <returns>A complete AST-independent module, including diagnostics for unsupported or unsafe declarations.</returns>
    public BindingModule Analyze(
        DeclarationGraph graph,
        IEnumerable<CppMacro>? macros = null,
        IEnumerable<FunctionAlias>? aliases = null
    ) {
        ArgumentNullException.ThrowIfNull(graph);
        BindingModuleBuilder module = new(this.m_config.apiName, this.m_config.@namespace, this.m_config.libName, this.m_config.resolvedTarget.targetId.value)
        {
            platformDefaultCallingConvention = this.m_config.resolvedTarget.platformId == "windows" && this.m_config.resolvedTarget.architectureId == "x86" ? "X86StdCall" : "C",
            importMode = this.m_config.importType switch
            {
                ImportType.DllImport => BindingImportMode.DllImport,
                ImportType.LibraryImport => BindingImportMode.LibraryImport,
                ImportType.FunctionTable => BindingImportMode.FunctionTable,
                _ => throw new ArgumentOutOfRangeException(nameof(this.m_config.importType), this.m_config.importType, null)
            },
            useCustomContext = this.m_config.useCustomContext,
            getLibraryNameFunctionName = this.m_config.getLibraryNameFunctionName,
            getLibraryExtensionFunctionName = this.m_config.getLibraryExtensionFunctionName,
            emitLibraryNameConstant = this.m_config.emitLibraryNameConstant,
            generateMetadata = this.m_config.generateMetadata,
            generatePlaceholderComments = this.m_config.generatePlaceholderComments,
            generateSizeOfStructs = this.m_config.generateSizeOfStructs,
            generateConstructorsForStructs = this.m_config.generateConstructorsForStructs,
            autoWrapCallbacks = this.m_config.autoWrapCallbacks,
            wrapPointersAsHandle = this.m_config.wrapPointersAsHandle,
            generateAdditionalOverloads = this.m_config.generateAdditionalOverloads,
            nestGeneratedTypesInApi = this.m_config.nestGeneratedTypesInApi
        };
        foreach (string @using in this.m_config.usings)
            module.usings.Add(@using);
        foreach (string varyingType in this.m_config.varyingTypes)
            module.varyingTypes.Add(varyingType);
        FunctionTableBuilder? functionTable = null;
        if (this.m_config.useFunctionTable)
        {
            functionTable = new();
            functionTable.Append(this.m_config.functionTableEntries.Select(entry => entry.Clone()).ToList());
        }

        HashSet<string> delegateNames = new(StringComparer.Ordinal);
        ILookup<string, FunctionAlias> functionAliases = ResolveFunctionAliases(aliases).ToLookup(alias => alias.exportedName, StringComparer.Ordinal);
        Dictionary<CppClass, string> opaqueRecordNames = new();
        DeclarationGraphNode[] orderedNodes = graph.TopologicalOrder().ToArray();
        foreach (CppTypedef typedef in orderedNodes.Select(node => node.declaration).OfType<CppTypedef>().Where(typedef => ShouldIncludeTypedef(typedef.name)))
        {
            CppType ultimateType = GetUltimateType(typedef);
            if (ultimateType is not CppClass { isDefinition: false } record || opaqueRecordNames.ContainsKey(record))
                continue;
            string canonicalName = this.m_config.GetManagedHandleName(typedef.name);
            opaqueRecordNames.Add(record, canonicalName);
            this.m_typeAnalyzer.RegisterManagedAlias(record, canonicalName);
            this.m_typeAnalyzer.RegisterManagedAlias(typedef, canonicalName);
        }

        foreach (DeclarationGraphNode node in orderedNodes)
        {
            try
            {
                switch (node.declaration)
                {
                    case CppClass cppClass when cppClass.classKind != CppClassKind.Class && this.m_config.generateTypes && (cppClass.isDefinition || this.m_config.generateHandles) && ShouldIncludeType(cppClass.name):
                        module.types.Add(!cppClass.isDefinition && opaqueRecordNames.TryGetValue(cppClass, out string? opaqueName) ? this.m_layoutAnalyzer.Analyze(cppClass, opaqueName) : this.m_layoutAnalyzer.Analyze(cppClass));
                        if (this.m_config.generateDelegates)
                            AnalyzeFieldDelegates(cppClass, module, delegateNames);
                        break;
                    case CppEnum cppEnum when this.m_config.generateEnums && ShouldIncludeEnum(cppEnum.name):
                        BindingTypeBuilder enumType = new(cppEnum.fullName, this.m_config.GetManagedEnumName(cppEnum.name), BindingTypeKind.Enumeration, cppEnum.integerType?.sizeOf ?? sizeof(int), cppEnum.integerType?.sizeOf ?? sizeof(int))
                        {
                            underlyingType = cppEnum.integerType == null ? null : this.m_typeAnalyzer.Analyze(cppEnum.integerType)
                        };
                        EnumPrefix enumPrefix = this.m_config.GetEnumNamePrefix(cppEnum.name);
                        foreach (CppEnumItem item in cppEnum.items)
                            enumType.enumMembers.Add(new(item.name, this.m_config.GetEnumNameEx(item.name, enumPrefix), item.value.ToString()));
                        module.types.Add(enumType);
                        break;
                    case CppTypedef typedef when ShouldIncludeTypedef(typedef.name):
                        if (TryGetDelegateType(typedef, out CppFunctionType? functionType))
                        {
                            if (this.m_config.generateDelegates && ShouldIncludeDelegate(typedef.name))
                                AddDelegate(module, delegateNames, typedef.name, this.m_config.GetDelegateName(typedef.name), functionType);
                            break;
                        }

                        if (!this.m_config.generateTypes && !typedef.IsOpaqueHandle())
                            break;
                        CppType ultimateType = GetUltimateType(typedef);
                        if (ultimateType is CppClass record && (!record.isDefinition || !graph.TryGetNode(record, out _)))
                        {
                            if (!this.m_config.generateHandles)
                                break;
                            string aliasName = this.m_config.GetManagedHandleName(typedef.name);
                            if (!opaqueRecordNames.TryGetValue(record, out string? canonicalName))
                            {
                                canonicalName = aliasName;
                                opaqueRecordNames.Add(record, canonicalName);
                                this.m_typeAnalyzer.RegisterManagedAlias(record, canonicalName);
                                module.types.Add(new(record.fullName, canonicalName, BindingTypeKind.Structure, Math.Max(0, record.sizeOf), Math.Max(1, record.alignOf)) { isOpaqueStorage = true });
                            }

                            this.m_typeAnalyzer.RegisterManagedAlias(typedef, canonicalName);
                            if (!string.Equals(aliasName, canonicalName, StringComparison.Ordinal))
                            {
                                module.types.Add(new(typedef.fullName, aliasName, BindingTypeKind.Alias, typedef.sizeOf, GetTypeAlignment(ultimateType)) { underlyingType = new(record.fullName, canonicalName, 0, false, record.sizeOf) });
                            }

                            break;
                        }

                        if (IsVoidRecord(typedef))
                        {
                            module.types.Add(new(typedef.fullName, this.m_config.GetManagedTypeName(typedef.name), BindingTypeKind.Structure, 0, 1) { isOpaqueStorage = true });
                            break;
                        }

                        bool opaqueHandle = typedef.IsOpaqueHandle();
                        if (opaqueHandle && !this.m_config.generateHandles)
                            break;
                        if (!opaqueHandle && this.m_config.autoSquashTypedef)
                            break;
                        module.types.Add(new(typedef.fullName, opaqueHandle ? this.m_config.GetManagedHandleName(typedef.name) : this.m_config.GetManagedTypeName(typedef.name), opaqueHandle ? BindingTypeKind.OpaqueHandle : BindingTypeKind.Alias, typedef.sizeOf, GetTypeAlignment(ultimateType)) { underlyingType = opaqueHandle ? null : this.m_typeAnalyzer.Analyze(ultimateType) });
                        break;
                    case CppFunction function when this.m_config.generateFunctions && ShouldIncludeFunction(function):
                        foreach (BindingFunctionBuilder analyzedFunction in AnalyzeFunctionVariants(function, graph.pointerSize))
                        {
                            if (functionTable != null)
                                analyzedFunction.functionTableIndex = functionTable.Add(function.name);
                            module.functions.Add(analyzedFunction);
                        }

                        foreach (FunctionAlias alias in functionAliases[function.name])
                        {
                            string aliasName = string.IsNullOrWhiteSpace(alias.friendlyName)
                                ? this.m_config.GetCsFunctionName(alias.exportedAliasName)
                                : this.m_config.GetCsCleanName(alias.friendlyName);
                            foreach (BindingFunctionBuilder analyzedAlias in AnalyzeFunctionVariants(function, graph.pointerSize, aliasName))
                            {
                                if (functionTable != null)
                                    analyzedAlias.functionTableIndex = functionTable.Add(function.name);
                                module.functions.Add(analyzedAlias);
                            }
                        }

                        break;
                }
            }
            catch (Exception exception)
            {
                string message = $"{node.declaration}: {exception.Message}";
                module.diagnostics.Add(message);
                module.structuredDiagnostics.Add(new(BindingDiagnosticSeverity.Error, message));
            }
        }

        AddCustomEnums(module);
        if (functionTable != null)
        {
            foreach (BGCS.Metadata.CsFunctionTableEntry entry in functionTable.entries.OrderBy(entry => entry.index))
                module.functionTableEntries.Add(new(entry.index, entry.entryPoint));
        }

        AddReferencedRecordClosure(module);
        AddSelfAliasStorageTypes(module);
        AnalyzeConstants(module, macros ?? []);
        new StrictSafetyAnalyzer(this.m_config).Analyze(module);
        return module.Freeze();
    }

    private void AddReferencedRecordClosure(BindingModuleBuilder module)
    {
        HashSet<string> defined = EnumerateTypes(module.types).Select(type => type.managedName).ToHashSet(StringComparer.Ordinal);
        foreach (TypeAnalyzer.ReferencedRecord reference in this.m_typeAnalyzer.referencedRecords.OrderBy(reference => reference.managedName, StringComparer.Ordinal))
        {
            if (!defined.Add(reference.managedName))
                continue;
            ExternalTypeContract? external = FindExternalTypeContract(reference);
            if (external != null)
            {
                bool byValue = !reference.behindPointerOnly;
                bool layoutMatches = reference.size > 0 && reference.size == external.size && reference.alignment == external.alignment;
                bool bypassed = byValue && external.byValuePolicy == ExternalTypeByValuePolicy.BypassLayoutValidation;
                bool accepted = !byValue || external.byValuePolicy == ExternalTypeByValuePolicy.RequireLayoutMatch && layoutMatches || bypassed;
                if (accepted)
                {
                    module.externalTypes.Add(new([reference.nativeName], reference.managedName, external.size, external.alignment, byValue, bypassed));
                    if (byValue)
                    {
                        string evidence = bypassed ? "native layout validation was explicitly bypassed" : $"native layout matched size {reference.size} and alignment {reference.alignment}";
                        module.structuredDiagnostics.Add(new(BindingDiagnosticSeverity.Warning, $"External managed carrier '{reference.managedName}' represents '{reference.nativeName}' by value; {evidence}. The project owns managed layout and runtime invocation evidence.", BindingDiagnosticCodes.C_EXTERNALTYPE));
                    }

                    continue;
                }

                string reason = external.byValuePolicy == ExternalTypeByValuePolicy.Reject ? "its ByValuePolicy is Reject" : $"parsed native layout {reference.size}/{reference.alignment} does not match declared carrier layout {external.size}/{external.alignment}";
                module.structuredDiagnostics.Add(new(BindingDiagnosticSeverity.Error, $"External managed carrier '{reference.managedName}' cannot represent '{reference.nativeName}' by value because {reason}. Use RequireLayoutMatch with the correct target layout, use a pointer, or deliberately select BypassLayoutValidation with independent native invocation tests.", BindingDiagnosticCodes.C_EXTERNALTYPE));
            }

            module.types.Add(new(reference.nativeName, reference.managedName, BindingTypeKind.Structure, reference.size, reference.alignment) { isOpaqueStorage = true });
            if (!reference.behindPointerOnly)
            {
                module.diagnostics.Add($"Referenced record '{reference.nativeName}' is unavailable as a definition but is used by value; only pointer use of synthesized opaque storage is ABI-safe.");
            }
        }
    }

    private ExternalTypeContract? FindExternalTypeContract(TypeAnalyzer.ReferencedRecord reference)
    {
        return this.m_config.externalTypeContracts.FirstOrDefault(contract => contract.managedTypes.Any(selector => ExternalTypeContract.MatchesSelector(selector, reference.managedName)) && contract.nativeTypes.Any(selector => ExternalTypeContract.MatchesSelector(NormalizeNativeType(selector), NormalizeNativeType(reference.nativeName))));
    }

    private static string NormalizeNativeType(string value)
    {
        string normalized = value.Trim();
        foreach (string prefix in new[]
        {
            "struct ",
            "union ",
            "class "
        }

        )
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return normalized[prefix.Length..].Trim();
        }

        return normalized;
    }

    private static IEnumerable<BindingType> EnumerateTypes(IEnumerable<BindingType> types)
    {
        foreach (BindingType type in types)
        {
            yield return type;
            foreach (BindingType nested in EnumerateTypes(type.nestedTypes))
                yield return nested;
        }
    }

    private void AnalyzeConstants(
        BindingModuleBuilder module,
        IEnumerable<CppMacro> macros
    ) {
        if (!this.m_config.generateConstants)
            return;
        Dictionary<string, BindingConstant> constants = new(StringComparer.Ordinal);
        foreach (CppMacro macro in macros)
        {
            if (macro.parameters is { Count: > 0 } || this.m_config.allowedConstants.Count != 0 && !this.m_config.allowedConstants.Contains(macro.name) || this.m_config.ignoredConstants.Contains(macro.name))
                continue;
            macro.UpdateValueFromTokens();
            string value = macro.value.NormalizeConstantValue().Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            BindingConstant? constant = CreateConstant(macro.name, value, constants);
            if (constant != null)
                constants[macro.name] = constant;
        }

        foreach (BindingConstant constant in constants.Values)
            module.constants.Add(constant);
    }

    private void AddCustomEnums(BindingModuleBuilder module)
    {
        if (!this.m_config.generateEnums)
            return;
        foreach (BGCS.Metadata.CsEnumMetadata custom in this.m_config.customEnums)
        {
            int size = custom.baseType switch
            {
                "byte" or "sbyte" => 1,
                "short" or "ushort" => 2,
                "int" or "uint" => 4,
                "long" or "ulong" => 8,
                _ => throw new InvalidOperationException($"Custom enum '{custom.name}' requires an integral base type; '{custom.baseType}' is unsupported.")
            };
            BindingTypeBuilder type = new(custom.cppName, custom.name, BindingTypeKind.Enumeration, size, size)
            {
                underlyingType = new(custom.baseType, custom.baseType, 0, false, size),
                comment = custom.comment,
                attributes = new List<string>(custom.attributes),
                isCustomDefinition = true
            };
            foreach (BGCS.Metadata.CsEnumItemMetadata item in custom.items)
            {
                type.enumMembers.Add(new(item.cppName, item.name ?? this.m_config.GetCsCleanName(item.cppName), item.value ?? item.cppValue) { comment = item.comment, attributes = new List<string>(item.attributes) });
            }

            module.types.Add(type);
        }
    }

    private static void AddSelfAliasStorageTypes(BindingModuleBuilder module)
    {
        HashSet<string> concreteTypeNames = module.types.Where(type => type.kind != BindingTypeKind.Alias).Select(type => type.managedName).ToHashSet(StringComparer.Ordinal);
        BindingType[] missingDefinitions = module.types.Where(type => type.kind == BindingTypeKind.Alias && type.underlyingType != null && !CsType.IsKnownPrimitive(type.managedName) && !CsType.IsKnownPrimitive(type.underlyingType.managedName) && string.Equals(type.managedName, type.underlyingType.managedName, StringComparison.Ordinal) && !concreteTypeNames.Contains(type.managedName)).Select(type => new BindingType(type.nativeName, type.managedName, BindingTypeKind.Structure, Math.Max(0, type.size), Math.Max(1, type.alignment)) { isOpaqueStorage = true }).ToArray();
        foreach (BindingType type in missingDefinitions)
        {
            if (concreteTypeNames.Add(type.managedName))
                module.types.Add(type);
        }
    }

    private BindingConstant? CreateConstant(
        string nativeName,
        string value,
        IReadOnlyDictionary<string, BindingConstant> constants
    ) {
        string managedName = this.m_config.GetConstantName(nativeName);
        if (value.IsNumeric(out NumberType numberType))
        {
            (string managedType, BindingConstantKind kind) = numberType switch
            {
                NumberType.Int => ("int", BindingConstantKind.Integer),
                NumberType.UInt => ("uint", BindingConstantKind.UnsignedInteger),
                NumberType.Long => ("long", BindingConstantKind.LongInteger),
                NumberType.ULong => ("ulong", BindingConstantKind.UnsignedLongInteger),
                NumberType.Float => ("float", BindingConstantKind.FloatingPoint),
                NumberType.Double => ("double", BindingConstantKind.FloatingPoint),
                NumberType.Decimal => ("decimal", BindingConstantKind.Decimal),
                _ => (string.Empty, BindingConstantKind.Custom)
            };
            return string.IsNullOrEmpty(managedType) ? null : new(nativeName, managedName, managedType, value, kind);
        }

        if (value.IsString())
            return new(nativeName, managedName, "string", value, BindingConstantKind.String);
        if (constants.TryGetValue(value, out BindingConstant? referenced))
            return new(nativeName, managedName, referenced.managedType, referenced.managedName, BindingConstantKind.Reference);
        return null;
    }

    private void AnalyzeFieldDelegates(
        CppClass cppClass,
        BindingModuleBuilder module,
        ISet<string> delegateNames
    ) {
        foreach (CppField field in cppClass.fields)
        {
            if (TryGetDelegateType(field.type, out CppFunctionType? functionType))
                AddDelegate(module, delegateNames, field.name, this.m_config.GetDelegateName(field.name), functionType);
        }

        foreach (CppClass nested in cppClass.classes)
            AnalyzeFieldDelegates(nested, module, delegateNames);
    }

    private void AddDelegate(
        BindingModuleBuilder module,
        ISet<string> delegateNames,
        string nativeName,
        string managedName,
        CppFunctionType functionType
    ) {
        string uniqueName = managedName;
        for (int suffix = 1; !delegateNames.Add(uniqueName); suffix++)
            uniqueName = managedName + suffix;
        BindingDelegateBuilder bindingDelegate = new(nativeName, uniqueName, this.m_typeAnalyzer.Analyze(functionType.returnType), this.m_ownershipAnalyzer.Analyze(functionType.returnType, Direction.Out, null))
        {
            callingConvention = functionType.callingConvention.ToString()
        };
        for (int i = 0; i < functionType.parameters.Count; i++)
        {
            CppParameter parameter = functionType.parameters[i];
            Direction direction = parameter.type.GetDirection();
            bindingDelegate.parameters.Add(new(parameter.name, this.m_config.GetParameterName(i, parameter.name), this.m_typeAnalyzer.Analyze(parameter.type), ToBindingDirection(direction), this.m_ownershipAnalyzer.Analyze(parameter.type, direction, null)));
        }

        module.delegates.Add(bindingDelegate);
    }

    private bool ShouldIncludeType(string name) => !string.IsNullOrWhiteSpace(name) && (this.m_config.allowedTypes.Count == 0 || this.m_config.allowedTypes.Contains(name)) && !this.m_config.ignoredTypes.Contains(name);
    private bool ShouldIncludeEnum(string name) => !string.IsNullOrWhiteSpace(name) && (this.m_config.allowedEnums.Count == 0 || this.m_config.allowedEnums.Contains(name)) && !this.m_config.ignoredEnums.Contains(name);
    private bool ShouldIncludeTypedef(string name) => !string.IsNullOrWhiteSpace(name) && (this.m_config.allowedTypedefs.Count == 0 || this.m_config.allowedTypedefs.Contains(name)) && !this.m_config.ignoredTypedefs.Contains(name);
    private bool ShouldIncludeDelegate(string name) => (this.m_config.allowedDelegates.Count == 0 || this.m_config.allowedDelegates.Contains(name)) && !this.m_config.ignoredDelegates.Contains(name);
    private bool ShouldIncludeFunction(CppFunction function) => function.IsPublicExport()
        && !function.flags.HasFlag(CppFunctionFlags.Inline)
        && (this.m_config.allowedFunctions.Count == 0 || this.m_config.allowedFunctions.Contains(function.name))
        && !this.m_config.ignoredFunctions.Contains(function.name);
    private static bool TryGetDelegateType(
        CppTypedef typedef,
        [NotNullWhen(true)] out CppFunctionType? functionType
    ) => TryGetDelegateType(typedef.elementType, out functionType);
    private static bool IsVoidRecord(CppTypedef typedef)
    {
        CppType type = typedef.elementType;
        while (true)
        {
            switch (type)
            {
                case CppQualifiedType qualified:
                    type = qualified.elementType;
                    continue;
                case CppTypedef nested:
                    type = nested.elementType;
                    continue;
            }

            break;
        }

        return type is not (CppPointerType or CppReferenceType) && string.Equals(type.GetDisplayName(), "void", StringComparison.Ordinal);
    }

    private static CppType GetUltimateType(CppTypedef typedef)
    {
        CppType type = typedef.elementType;
        while (type is CppTypedef nested)
            type = nested.elementType;
        return type;
    }

    private static int GetTypeAlignment(CppType type)
    {
        while (type is CppQualifiedType qualified)
            type = qualified.elementType;
        return type switch
        {
            CppClass record => Math.Max(1, record.alignOf),
            CppPointerType or CppReferenceType or CppFunctionType => Math.Max(1, type.sizeOf),
            _ => Math.Clamp(type.sizeOf, 1, 8)
        };
    }

    private static bool TryGetDelegateType(
        CppType type,
        [NotNullWhen(true)] out CppFunctionType? functionType
    ) {
        while (type is CppTypedef typedef)
            type = typedef.elementType;
        if (type is CppPointerType { elementType: CppFunctionType function })
        {
            functionType = function;
            return true;
        }

        functionType = null;
        return false;
    }

    private IEnumerable<FunctionAlias> ResolveFunctionAliases(IEnumerable<FunctionAlias>? aliases)
    {
        Dictionary<(string Function, string Alias), FunctionAlias> resolved = [];
        foreach (FunctionAlias alias in aliases ?? [])
            resolved.TryAdd((alias.exportedName, alias.exportedAliasName), alias);
        foreach (FunctionAliasMapping mapping in this.m_config.functionAliasMappings.Values.SelectMany(group => group))
        {
            resolved[(mapping.exportedName, mapping.exportedAliasName)] = new(mapping.exportedName, mapping.exportedAliasName,
                mapping.friendlyName ?? this.m_config.GetCsFunctionName(mapping.exportedAliasName), mapping.comment);
        }
        return resolved.Values;
    }

    private BindingFunctionBuilder AnalyzeFunction(
        CppFunction function,
        string? aliasName = null
    ) {
        this.m_config.marshallingMappings.TryGetValue(function.name, out FunctionMarshallingMapping? marshallingMapping);
        this.m_config.TryGetFunctionMapping(function.name, out FunctionMapping? functionMapping);
        BindingTypeReference returnType = this.m_typeAnalyzer.Analyze(function.returnType);
        string rawManagedName = aliasName ?? this.m_config.GetCsFunctionName(function.name);
        ResolveManagedPresentation(function, rawManagedName, functionMapping, out string managedName, out string managedContainer, out BindingManagedFunctionKind managedKind, out string? receiverType, out int? receiverIndex);
        BindingFunctionBuilder bindingFunction = new(function.name, managedName, GetFunctionKind(function), returnType, this.m_ownershipAnalyzer.Analyze(function.returnType, Direction.Out, marshallingMapping?.@return))
        {
            rawManagedName = rawManagedName,
            callingConvention = function.callingConvention.ToString(),
            isVariadic = function.flags.HasFlag(CppFunctionFlags.Variadic),
            declaringType = (function.parent as CppClass)?.fullName,
            managedContainer = managedContainer,
            managedKind = managedKind,
            managedReceiverType = receiverType,
            managedReceiverIndex = receiverIndex
        };
        for (int i = 0; i < function.parameters.Count; i++)
        {
            CppParameter parameter = function.parameters[i];
            Direction direction = parameter.type.GetDirection();
            ParameterMapping? friendlyMapping = functionMapping?.parameters?.FirstOrDefault(candidate => string.Equals(candidate.exportedName, parameter.name, StringComparison.Ordinal));
            if (friendlyMapping?.useOut == true)
                direction = Direction.Out;
            MarshallingMapping? parameterMapping = null;
            marshallingMapping?.parameters.TryGetValue(parameter.name, out parameterMapping);
            string managedParameterName = string.IsNullOrWhiteSpace(friendlyMapping?.friendlyName) ? this.m_config.GetParameterName(i, parameter.name) : this.m_config.GetParameterName(i, friendlyMapping.friendlyName);
            string? defaultValue = this.m_config.TryGetDefaultValue(function.name, parameter, out string? configuredDefault) ? configuredDefault : null;
            bindingFunction.parameters.Add(new(parameter.name, managedParameterName, this.m_typeAnalyzer.Analyze(parameter.type), ToBindingDirection(direction), this.m_ownershipAnalyzer.Analyze(parameter.type, direction, parameterMapping)) { defaultValue = defaultValue });
        }

        this.m_overloadPlanner.Plan(bindingFunction);
        return bindingFunction;
    }

    private IReadOnlyList<BindingFunctionBuilder> AnalyzeFunctionVariants(
        CppFunction function,
        int pointerSize,
        string? aliasName = null
    ) {
        BindingFunctionBuilder analyzed = AnalyzeFunction(function, aliasName);
        if (!analyzed.isVariadic || !this.m_config.variadicFunctionVariants.TryGetValue(function.name, out List<VariadicFunctionVariant>? variants) || variants.Count == 0)
            return [analyzed];
        List<BindingFunctionBuilder> expanded = [];
        foreach (VariadicFunctionVariant variant in variants)
        {
            if (variant.parameterTypes.Count == 0)
                throw new InvalidOperationException($"Variadic function '{function.name}' has a variant without fixed parameter types.");
            string suffix = this.m_config.GetCsCleanName(variant.suffix);
            if (string.IsNullOrWhiteSpace(suffix))
                throw new InvalidOperationException($"Variadic function '{function.name}' has a variant without a suffix.");
            BindingFunctionBuilder fixedFunction = new(analyzed.nativeName, analyzed.managedName + suffix, analyzed.kind, analyzed.returnType, analyzed.returnMarshalling)
            {
                rawManagedName = analyzed.rawManagedName + suffix,
                callingConvention = analyzed.callingConvention,
                isVariadic = false,
                declaringType = analyzed.declaringType,
                managedContainer = analyzed.managedContainer,
                managedKind = analyzed.managedKind,
                managedReceiverType = analyzed.managedReceiverType,
                managedReceiverIndex = analyzed.managedReceiverIndex
            };
            foreach (BindingParameter parameter in analyzed.parameters)
                fixedFunction.parameters.Add(parameter);
            for (int index = 0; index < variant.parameterTypes.Count; index++)
            {
                string managedType = variant.parameterTypes[index].Trim();
                if (managedType.Length == 0)
                    throw new InvalidOperationException($"Variadic function '{function.name}' has an empty promoted parameter type.");
                string configuredName = index < variant.parameterNames.Count ? variant.parameterNames[index] : $"arg{index}";
                string name = this.m_config.GetParameterName(analyzed.parameters.Count + index, configuredName);
                fixedFunction.parameters.Add(new(configuredName, name, CreateConfiguredTypeReference(managedType, pointerSize), BindingDirection.In, new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)));
            }

            this.m_overloadPlanner.Plan(fixedFunction);
            expanded.Add(fixedFunction);
        }

        return expanded;
    }

    private static BindingTypeReference CreateConfiguredTypeReference(
        string managedType,
        int pointerSize
    ) {
        int pointerDepth = managedType.Reverse().TakeWhile(character => character == '*').Count();
        string carrier = managedType[..(managedType.Length - pointerDepth)].TrimEnd();
        int size = pointerDepth > 0 || carrier is "nint" or "nuint" ? pointerSize : carrier switch
        {
            "bool" or "byte" or "sbyte" => 1,
            "char" or "short" or "ushort" => 2,
            "int" or "uint" or "float" => 4,
            "long" or "ulong" or "double" => 8,
            _ => 0
        };
        return new(managedType, managedType, pointerDepth, false, size);
    }

    private void ResolveManagedPresentation(
        CppFunction function,
        string rawManagedName,
        FunctionMapping? mapping,
        out string managedName,
        out string managedContainer,
        out BindingManagedFunctionKind managedKind,
        out string? receiverType,
        out int? receiverIndex
    ) {
        managedName = rawManagedName;
        managedContainer = string.IsNullOrWhiteSpace(mapping?.containerName) ? this.m_config.apiName : this.m_config.GetCsCleanName(mapping.containerName);
        managedKind = BindingManagedFunctionKind.Static;
        receiverType = null;
        receiverIndex = null;
        foreach ((string nativeType, List<string> functions) in this.m_config.knownMemberFunctions)
        {
            if (!functions.Contains(function.name, StringComparer.Ordinal) || function.parameters.Count == 0)
                continue;
            string expectedReceiverType = this.m_config.GetManagedTypeName(nativeType);
            BindingTypeReference actualReceiver = this.m_typeAnalyzer.Analyze(function.parameters[0].type);
            string actualReceiverType = actualReceiver.managedName.TrimEnd('*').TrimEnd();
            if (!string.Equals(expectedReceiverType, actualReceiverType, StringComparison.Ordinal))
                continue;
            managedKind = BindingManagedFunctionKind.Instance;
            receiverType = expectedReceiverType;
            receiverIndex = 0;
            managedContainer = receiverType;
            return;
        }

        if (!this.m_config.generateExtensions || function.parameters.Count == 0 || this.m_config.allowedExtensions.Count != 0 && !this.m_config.allowedExtensions.Contains(function.name) || this.m_config.ignoredExtensions.Contains(function.name) || !TryGetExtensionReceiverName(function.parameters[0].type, out string? nativeReceiverName))
            return;
        managedKind = BindingManagedFunctionKind.Extension;
        receiverType = this.m_typeAnalyzer.Analyze(function.parameters[0].type).managedName;
        receiverIndex = 0;
        managedContainer = "Extensions";
        managedName = this.m_config.GetExtensionName(rawManagedName, this.m_config.GetExtensionNamePrefix(nativeReceiverName));
    }

    private static bool TryGetExtensionReceiverName(
        CppType type,
        [NotNullWhen(true)] out string? nativeName
    ) {
        while (type is CppQualifiedType qualified)
            type = qualified.elementType;
        if (type is CppTypedef typedef && typedef.IsOpaqueHandle())
        {
            nativeName = typedef.name;
            return true;
        }

        nativeName = null;
        return false;
    }

    private static BindingDirection ToBindingDirection(Direction direction)
    {
        return direction switch
        {
            Direction.In => BindingDirection.In,
            Direction.Out => BindingDirection.Out,
            Direction.InOut => BindingDirection.InOut,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }

    private static BindingFunctionKind GetFunctionKind(CppFunction function)
    {
        if (function.flags.HasFlag(CppFunctionFlags.Constructor))
            return BindingFunctionKind.Constructor;
        if (function.flags.HasFlag(CppFunctionFlags.Destructor))
            return BindingFunctionKind.Destructor;
        if (function.isCxxClassMethod && (function.storageQualifier & CppStorageQualifier.Static) != 0)
            return BindingFunctionKind.Static;
        return function.flags.HasFlag(CppFunctionFlags.Method) ? BindingFunctionKind.Instance : BindingFunctionKind.Free;
    }
}
