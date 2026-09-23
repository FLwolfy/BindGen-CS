namespace BGCS.Analysis;

using System.Diagnostics.CodeAnalysis;
using BGCS.Core;
using BGCS.Core.CSharp;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
using BGCS.Core.Mapping;
using BGCS.Intermediate;

/// <summary>
/// Lowers a declaration dependency graph into the shared binding intermediate representation.
/// </summary>
public sealed class BindingModuleAnalyzer
{
    private readonly CsCodeGeneratorConfig config;
    private readonly TypeAnalyzer typeAnalyzer;
    private readonly AbiLayoutAnalyzer layoutAnalyzer;
    private readonly OwnershipAnalyzer ownershipAnalyzer;
    private readonly OverloadPlanner overloadPlanner;

    public BindingModuleAnalyzer(CsCodeGeneratorConfig config)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        typeAnalyzer = new(config);
        layoutAnalyzer = new(config, typeAnalyzer);
        ownershipAnalyzer = new(config);
        overloadPlanner = new();
    }

    public BindingModule Analyze(DeclarationGraph graph, IEnumerable<CppMacro>? macros = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        BindingModule module = new(config.ApiName, config.Namespace, config.LibName,
            config.ResolvedTarget.Identifier)
        {
            ImportMode = config.ImportType switch
            {
                ImportType.DllImport => BindingImportMode.DllImport,
                ImportType.LibraryImport => BindingImportMode.LibraryImport,
                ImportType.FunctionTable => BindingImportMode.FunctionTable,
                _ => throw new ArgumentOutOfRangeException(nameof(config.ImportType), config.ImportType, null)
            },
            UseCustomContext = config.UseCustomContext,
            GetLibraryNameFunctionName = config.GetLibraryNameFunctionName,
            GetLibraryExtensionFunctionName = config.GetLibraryExtensionFunctionName,
            EmitLibraryNameConstant = config.EmitLibraryNameConstant,
            GenerateMetadata = config.GenerateMetadata,
            GeneratePlaceholderComments = config.GeneratePlaceholderComments,
            GenerateSizeOfStructs = config.GenerateSizeOfStructs,
            GenerateConstructorsForStructs = config.GenerateConstructorsForStructs,
            AutoWrapCallbacks = config.AutoWrapCallbacks,
            WrapPointersAsHandle = config.WrapPointersAsHandle,
            GenerateAdditionalOverloads = config.GenerateAdditionalOverloads,
            NestGeneratedTypesInApi = config.NestGeneratedTypesInApi
        };
        foreach (string @using in config.Usings)
            module.Usings.Add(@using);
        foreach (string varyingType in config.VaryingTypes)
            module.VaryingTypes.Add(varyingType);
        FunctionTableBuilder? functionTable = null;
        if (config.UseFunctionTable)
        {
            functionTable = new();
            functionTable.Append(config.FunctionTableEntries.Select(entry => entry.Clone()).ToList());
        }
        HashSet<string> delegateNames = new(StringComparer.Ordinal);
        Dictionary<CppClass, string> opaqueRecordNames = new();
        foreach (DeclarationGraphNode node in graph.TopologicalOrder())
        {
            try
            {
                switch (node.Declaration)
                {
                    case CppClass cppClass when cppClass.ClassKind != CppClassKind.Class && config.GenerateTypes &&
                        (cppClass.IsDefinition || config.GenerateHandles) && ShouldIncludeType(cppClass.Name):
                        module.Types.Add(layoutAnalyzer.Analyze(cppClass));
                        AnalyzeFieldDelegates(cppClass, module, delegateNames);
                        break;
                    case CppEnum cppEnum when config.GenerateEnums && ShouldIncludeEnum(cppEnum.Name):
                        BindingType enumType = new(cppEnum.FullName, config.GetManagedEnumName(cppEnum.Name),
                            BindingTypeKind.Enumeration, cppEnum.IntegerType?.SizeOf ?? sizeof(int),
                            cppEnum.IntegerType?.SizeOf ?? sizeof(int))
                        {
                            UnderlyingType = cppEnum.IntegerType == null ? null : typeAnalyzer.Analyze(cppEnum.IntegerType)
                        };
                        EnumPrefix enumPrefix = config.GetEnumNamePrefix(cppEnum.Name);
                        foreach (CppEnumItem item in cppEnum.Items)
                            enumType.EnumMembers.Add(new(item.Name, config.GetEnumNameEx(item.Name, enumPrefix), item.Value.ToString()));
                        module.Types.Add(enumType);
                        break;
                    case CppTypedef typedef when ShouldIncludeTypedef(typedef.Name):
                        if (TryGetDelegateType(typedef, out CppFunctionType? functionType))
                        {
                            if (config.GenerateDelegates && ShouldIncludeDelegate(typedef.Name))
                                AddDelegate(module, delegateNames, typedef.Name, config.GetDelegateName(typedef.Name), functionType);
                            break;
                        }
                        if (!config.GenerateTypes && !typedef.IsOpaqueHandle())
                            break;
                        CppType ultimateType = GetUltimateType(typedef);
                        if (ultimateType is CppClass record &&
                            (!record.IsDefinition || !graph.TryGetNode(record, out _)))
                        {
                            if (!config.GenerateHandles)
                                break;
                            string aliasName = config.GetManagedHandleName(typedef.Name);
                            if (!opaqueRecordNames.TryGetValue(record, out string? canonicalName))
                            {
                                canonicalName = aliasName;
                                opaqueRecordNames.Add(record, canonicalName);
                                typeAnalyzer.RegisterManagedAlias(record, canonicalName);
                                module.Types.Add(new(record.FullName, canonicalName, BindingTypeKind.Structure,
                                    Math.Max(0, record.SizeOf), Math.Max(1, record.AlignOf))
                                {
                                    IsOpaqueStorage = true
                                });
                            }
                            typeAnalyzer.RegisterManagedAlias(typedef, canonicalName);
                            if (!string.Equals(aliasName, canonicalName, StringComparison.Ordinal))
                            {
                                module.Types.Add(new(typedef.FullName, aliasName, BindingTypeKind.Alias,
                                    typedef.SizeOf, GetTypeAlignment(ultimateType))
                                {
                                    UnderlyingType = new(record.FullName, canonicalName, 0, false, record.SizeOf)
                                });
                            }
                            break;
                        }
                        if (IsVoidRecord(typedef))
                        {
                            module.Types.Add(new(typedef.FullName, config.GetManagedTypeName(typedef.Name),
                                BindingTypeKind.Structure, 0, 1)
                            {
                                IsOpaqueStorage = true
                            });
                            break;
                        }
                        bool opaqueHandle = typedef.IsOpaqueHandle();
                        if (opaqueHandle && !config.GenerateHandles)
                            break;
                        if (!opaqueHandle && config.AutoSquashTypedef)
                            break;
                        module.Types.Add(new(typedef.FullName,
                            opaqueHandle ? config.GetManagedHandleName(typedef.Name) : config.GetManagedTypeName(typedef.Name),
                            opaqueHandle ? BindingTypeKind.OpaqueHandle : BindingTypeKind.Alias,
                            typedef.SizeOf, GetTypeAlignment(ultimateType))
                        {
                            UnderlyingType = opaqueHandle ? null : typeAnalyzer.Analyze(ultimateType)
                        });
                        break;
                    case CppFunction function when config.GenerateFunctions && ShouldIncludeFunction(function):
                        foreach (BindingFunction analyzedFunction in AnalyzeFunctionVariants(function))
                        {
                            if (functionTable != null)
                                analyzedFunction.FunctionTableIndex = functionTable.Add(function.Name);
                            module.Functions.Add(analyzedFunction);
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                module.Diagnostics.Add($"{node.Declaration}: {exception.Message}");
            }
        }
        AddCustomEnums(module);
        if (functionTable != null)
        {
            foreach (BGCS.Metadata.CsFunctionTableEntry entry in functionTable.Entries.OrderBy(entry => entry.Index))
                module.FunctionTableEntries.Add(new(entry.Index, entry.EntryPoint));
        }
        AddReferencedRecordClosure(module);
        AddSelfAliasStorageTypes(module);
        AnalyzeConstants(module, macros ?? []);
        new StrictSafetyAnalyzer(config).Analyze(module);
        return module;
    }

    private void AddReferencedRecordClosure(BindingModule module)
    {
        HashSet<string> defined = EnumerateTypes(module.Types)
            .Select(type => type.ManagedName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (TypeAnalyzer.ReferencedRecord reference in typeAnalyzer.ReferencedRecords
                     .OrderBy(reference => reference.ManagedName, StringComparer.Ordinal))
        {
            if (!defined.Add(reference.ManagedName))
                continue;
            module.Types.Add(new(reference.NativeName, reference.ManagedName, BindingTypeKind.Structure,
                reference.Size, reference.Alignment)
            {
                IsOpaqueStorage = true
            });
            if (!reference.BehindPointerOnly)
            {
                module.Diagnostics.Add(
                    $"Referenced record '{reference.NativeName}' is unavailable as a definition but is used by value; only pointer use of synthesized opaque storage is ABI-safe.");
            }
        }
    }

    private static IEnumerable<BindingType> EnumerateTypes(IEnumerable<BindingType> types)
    {
        foreach (BindingType type in types)
        {
            yield return type;
            foreach (BindingType nested in EnumerateTypes(type.NestedTypes))
                yield return nested;
        }
    }

    private void AnalyzeConstants(BindingModule module, IEnumerable<CppMacro> macros)
    {
        if (!config.GenerateConstants)
            return;
        Dictionary<string, BindingConstant> constants = new(StringComparer.Ordinal);
        foreach (CppMacro macro in macros)
        {
            if (macro.Parameters is { Count: > 0 } ||
                config.AllowedConstants.Count != 0 && !config.AllowedConstants.Contains(macro.Name) ||
                config.IgnoredConstants.Contains(macro.Name))
                continue;
            macro.UpdateValueFromTokens();
            string value = macro.Value.NormalizeConstantValue().Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            BindingConstant? constant = CreateConstant(macro.Name, value, constants);
            if (constant != null)
                constants[macro.Name] = constant;
        }
        foreach (BindingConstant constant in constants.Values)
            module.Constants.Add(constant);
    }

    private void AddCustomEnums(BindingModule module)
    {
        if (!config.GenerateEnums)
            return;
        foreach (BGCS.Metadata.CsEnumMetadata custom in config.CustomEnums)
        {
            BindingType type = new(custom.CppName, custom.Name, BindingTypeKind.Enumeration, sizeof(int), sizeof(int))
            {
                UnderlyingType = new(custom.BaseType, custom.BaseType, 0, false, sizeof(int)),
                Comment = custom.Comment,
                Attributes = new List<string>(custom.Attributes),
                IsCustomDefinition = true
            };
            foreach (BGCS.Metadata.CsEnumItemMetadata item in custom.Items)
            {
                type.EnumMembers.Add(new(item.CppName, item.Name ?? config.GetCsCleanName(item.CppName),
                    item.Value ?? item.CppValue)
                {
                    Comment = item.Comment,
                    Attributes = new List<string>(item.Attributes)
                });
            }
            module.Types.Add(type);
        }
    }

    private static void AddSelfAliasStorageTypes(BindingModule module)
    {
        HashSet<string> concreteTypeNames = module.Types
            .Where(type => type.Kind != BindingTypeKind.Alias)
            .Select(type => type.ManagedName)
            .ToHashSet(StringComparer.Ordinal);
        BindingType[] missingDefinitions = module.Types
            .Where(type => type.Kind == BindingTypeKind.Alias && type.UnderlyingType != null &&
                !CsType.IsKnownPrimitive(type.ManagedName) &&
                !CsType.IsKnownPrimitive(type.UnderlyingType.ManagedName) &&
                string.Equals(type.ManagedName, type.UnderlyingType.ManagedName, StringComparison.Ordinal) &&
                !concreteTypeNames.Contains(type.ManagedName))
            .Select(type => new BindingType(type.NativeName, type.ManagedName, BindingTypeKind.Structure,
                Math.Max(0, type.Size), Math.Max(1, type.Alignment))
            {
                IsOpaqueStorage = true
            })
            .ToArray();
        foreach (BindingType type in missingDefinitions)
        {
            if (concreteTypeNames.Add(type.ManagedName))
                module.Types.Add(type);
        }
    }

    private BindingConstant? CreateConstant(string nativeName, string value,
        IReadOnlyDictionary<string, BindingConstant> constants)
    {
        string managedName = config.GetConstantName(nativeName);
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
            return string.IsNullOrEmpty(managedType)
                ? null
                : new(nativeName, managedName, managedType, value, kind);
        }
        if (value.IsString())
            return new(nativeName, managedName, "string", value, BindingConstantKind.String);
        if (constants.TryGetValue(value, out BindingConstant? referenced))
            return new(nativeName, managedName, referenced.ManagedType, referenced.ManagedName, BindingConstantKind.Reference);
        return null;
    }

    private void AnalyzeFieldDelegates(CppClass cppClass, BindingModule module, ISet<string> delegateNames)
    {
        foreach (CppField field in cppClass.Fields)
        {
            if (TryGetDelegateType(field.Type, out CppFunctionType? functionType))
                AddDelegate(module, delegateNames, field.Name, config.GetDelegateName(field.Name), functionType);
        }
        foreach (CppClass nested in cppClass.Classes)
            AnalyzeFieldDelegates(nested, module, delegateNames);
    }

    private void AddDelegate(BindingModule module, ISet<string> delegateNames, string nativeName,
        string managedName, CppFunctionType functionType)
    {
        string uniqueName = managedName;
        for (int suffix = 1; !delegateNames.Add(uniqueName); suffix++)
            uniqueName = managedName + suffix;
        BindingDelegate bindingDelegate = new(nativeName, uniqueName, typeAnalyzer.Analyze(functionType.ReturnType),
            ownershipAnalyzer.Analyze(functionType.ReturnType, Direction.Out, null))
        {
            CallingConvention = functionType.CallingConvention.ToString()
        };
        for (int i = 0; i < functionType.Parameters.Count; i++)
        {
            CppParameter parameter = functionType.Parameters[i];
            Direction direction = parameter.Type.GetDirection();
            bindingDelegate.Parameters.Add(new(parameter.Name, config.GetParameterName(i, parameter.Name),
                typeAnalyzer.Analyze(parameter.Type), ToBindingDirection(direction),
                ownershipAnalyzer.Analyze(parameter.Type, direction, null)));
        }
        module.Delegates.Add(bindingDelegate);
    }

    private bool ShouldIncludeType(string name) => !string.IsNullOrWhiteSpace(name) &&
        (config.AllowedTypes.Count == 0 || config.AllowedTypes.Contains(name)) && !config.IgnoredTypes.Contains(name);

    private bool ShouldIncludeEnum(string name) => !string.IsNullOrWhiteSpace(name) &&
        (config.AllowedEnums.Count == 0 || config.AllowedEnums.Contains(name)) && !config.IgnoredEnums.Contains(name);

    private bool ShouldIncludeTypedef(string name) => !string.IsNullOrWhiteSpace(name) &&
        (config.AllowedTypedefs.Count == 0 || config.AllowedTypedefs.Contains(name)) && !config.IgnoredTypedefs.Contains(name);

    private bool ShouldIncludeDelegate(string name) =>
        (config.AllowedDelegates.Count == 0 || config.AllowedDelegates.Contains(name)) &&
        !config.IgnoredDelegates.Contains(name);

    private bool ShouldIncludeFunction(CppFunction function) => function.IsPublicExport() &&
        function.Flags != CppFunctionFlags.Inline &&
        (config.AllowedFunctions.Count == 0 || config.AllowedFunctions.Contains(function.Name)) &&
        !config.IgnoredFunctions.Contains(function.Name);

    private static bool TryGetDelegateType(CppTypedef typedef, [NotNullWhen(true)] out CppFunctionType? functionType) =>
        TryGetDelegateType(typedef.ElementType, out functionType);

    private static bool IsVoidRecord(CppTypedef typedef)
    {
        CppType type = typedef.ElementType;
        while (true)
        {
            switch (type)
            {
                case CppQualifiedType qualified:
                    type = qualified.ElementType;
                    continue;
                case CppTypedef nested:
                    type = nested.ElementType;
                    continue;
            }
            break;
        }
        return type is not (CppPointerType or CppReferenceType) &&
            string.Equals(type.GetDisplayName(), "void", StringComparison.Ordinal);
    }

    private static CppType GetUltimateType(CppTypedef typedef)
    {
        CppType type = typedef.ElementType;
        while (type is CppTypedef nested)
            type = nested.ElementType;
        return type;
    }

    private static int GetTypeAlignment(CppType type)
    {
        while (type is CppQualifiedType qualified)
            type = qualified.ElementType;
        return type switch
        {
            CppClass record => Math.Max(1, record.AlignOf),
            CppPointerType or CppReferenceType or CppFunctionType => Math.Max(1, type.SizeOf),
            _ => Math.Clamp(type.SizeOf, 1, 8)
        };
    }

    private static bool TryGetDelegateType(CppType type, [NotNullWhen(true)] out CppFunctionType? functionType)
    {
        while (type is CppTypedef typedef)
            type = typedef.ElementType;
        if (type is CppPointerType { ElementType: CppFunctionType function })
        {
            functionType = function;
            return true;
        }
        functionType = null;
        return false;
    }

    private BindingFunction AnalyzeFunction(CppFunction function)
    {
        config.MarshallingMappings.TryGetValue(function.Name, out FunctionMarshallingMapping? marshallingMapping);
        config.TryGetFunctionMapping(function.Name, out FunctionMapping? functionMapping);
        BindingTypeReference returnType = typeAnalyzer.Analyze(function.ReturnType);
        string rawManagedName = config.GetCsFunctionName(function.Name);
        ResolveManagedPresentation(function, rawManagedName, functionMapping,
            out string managedName, out string managedContainer, out BindingManagedFunctionKind managedKind,
            out string? receiverType, out int? receiverIndex);
        BindingFunction bindingFunction = new(function.Name, managedName,
            GetFunctionKind(function), returnType, ownershipAnalyzer.Analyze(function.ReturnType, Direction.Out, marshallingMapping?.Return))
        {
            RawManagedName = rawManagedName,
            CallingConvention = function.CallingConvention.ToString(),
            IsVariadic = function.Flags.HasFlag(CppFunctionFlags.Variadic),
            DeclaringType = (function.Parent as CppClass)?.FullName,
            ManagedContainer = managedContainer,
            ManagedKind = managedKind,
            ManagedReceiverType = receiverType,
            ManagedReceiverIndex = receiverIndex
        };
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            CppParameter parameter = function.Parameters[i];
            Direction direction = parameter.Type.GetDirection();
            ParameterMapping? friendlyMapping = functionMapping?.Parameters?.FirstOrDefault(candidate =>
                string.Equals(candidate.ExportedName, parameter.Name, StringComparison.Ordinal));
            if (friendlyMapping?.UseOut == true)
                direction = Direction.Out;
            MarshallingMapping? parameterMapping = null;
            marshallingMapping?.Parameters.TryGetValue(parameter.Name, out parameterMapping);
            string managedParameterName = string.IsNullOrWhiteSpace(friendlyMapping?.FriendlyName)
                ? config.GetParameterName(i, parameter.Name)
                : config.GetParameterName(i, friendlyMapping.FriendlyName);
            string? defaultValue = config.TryGetDefaultValue(function.Name, parameter, false, out string? configuredDefault)
                ? configuredDefault
                : null;
            bindingFunction.Parameters.Add(new(parameter.Name, managedParameterName,
                typeAnalyzer.Analyze(parameter.Type), ToBindingDirection(direction), ownershipAnalyzer.Analyze(parameter.Type, direction, parameterMapping))
            {
                DefaultValue = defaultValue
            });
        }
        overloadPlanner.Plan(bindingFunction);
        return bindingFunction;
    }

    private IReadOnlyList<BindingFunction> AnalyzeFunctionVariants(CppFunction function)
    {
        BindingFunction analyzed = AnalyzeFunction(function);
        if (!analyzed.IsVariadic || !config.VariadicFunctionVariants.TryGetValue(function.Name,
                out List<VariadicFunctionVariant>? variants) || variants.Count == 0)
            return [analyzed];

        List<BindingFunction> expanded = [];
        foreach (VariadicFunctionVariant variant in variants)
        {
            if (variant.ParameterTypes.Count == 0)
                throw new InvalidOperationException($"Variadic function '{function.Name}' has a variant without fixed parameter types.");
            string suffix = config.GetCsCleanName(variant.Suffix);
            if (string.IsNullOrWhiteSpace(suffix))
                throw new InvalidOperationException($"Variadic function '{function.Name}' has a variant without a suffix.");
            BindingFunction fixedFunction = new(analyzed.NativeName, analyzed.ManagedName + suffix,
                analyzed.Kind, analyzed.ReturnType, analyzed.ReturnMarshalling)
            {
                RawManagedName = analyzed.RawManagedName + suffix,
                CallingConvention = analyzed.CallingConvention,
                IsVariadic = false,
                DeclaringType = analyzed.DeclaringType,
                ManagedContainer = analyzed.ManagedContainer,
                ManagedKind = analyzed.ManagedKind,
                ManagedReceiverType = analyzed.ManagedReceiverType,
                ManagedReceiverIndex = analyzed.ManagedReceiverIndex
            };
            foreach (BindingParameter parameter in analyzed.Parameters)
                fixedFunction.Parameters.Add(parameter);
            for (int index = 0; index < variant.ParameterTypes.Count; index++)
            {
                string managedType = variant.ParameterTypes[index].Trim();
                if (managedType.Length == 0)
                    throw new InvalidOperationException($"Variadic function '{function.Name}' has an empty promoted parameter type.");
                string configuredName = index < variant.ParameterNames.Count
                    ? variant.ParameterNames[index]
                    : $"arg{index}";
                string name = config.GetParameterName(analyzed.Parameters.Count + index, configuredName);
                fixedFunction.Parameters.Add(new(configuredName, name, CreateConfiguredTypeReference(managedType),
                    BindingDirection.In, new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed)));
            }
            overloadPlanner.Plan(fixedFunction);
            expanded.Add(fixedFunction);
        }
        return expanded;
    }

    private static BindingTypeReference CreateConfiguredTypeReference(string managedType)
    {
        int pointerDepth = managedType.Reverse().TakeWhile(character => character == '*').Count();
        string carrier = managedType[..(managedType.Length - pointerDepth)].TrimEnd();
        int size = pointerDepth > 0 || carrier is "nint" or "nuint"
            ? IntPtr.Size
            : carrier switch
            {
                "bool" or "byte" or "sbyte" => 1,
                "char" or "short" or "ushort" => 2,
                "int" or "uint" or "float" => 4,
                "long" or "ulong" or "double" => 8,
                _ => 0
            };
        return new(managedType, managedType, pointerDepth, false, size);
    }

    private void ResolveManagedPresentation(CppFunction function, string rawManagedName, FunctionMapping? mapping,
        out string managedName, out string managedContainer, out BindingManagedFunctionKind managedKind,
        out string? receiverType, out int? receiverIndex)
    {
        managedName = rawManagedName;
        managedContainer = string.IsNullOrWhiteSpace(mapping?.ContainerName)
            ? config.ApiName
            : config.GetCsCleanName(mapping.ContainerName);
        managedKind = BindingManagedFunctionKind.Static;
        receiverType = null;
        receiverIndex = null;

        foreach ((string nativeType, List<string> functions) in config.KnownMemberFunctions)
        {
            if (!functions.Contains(function.Name, StringComparer.Ordinal) || function.Parameters.Count == 0)
                continue;
            managedKind = BindingManagedFunctionKind.Instance;
            receiverType = config.GetManagedTypeName(nativeType);
            receiverIndex = 0;
            managedContainer = receiverType;
            return;
        }

        if (!config.GenerateExtensions || function.Parameters.Count == 0 ||
            config.AllowedExtensions.Count != 0 && !config.AllowedExtensions.Contains(function.Name) ||
            config.IgnoredExtensions.Contains(function.Name) ||
            !TryGetExtensionReceiverName(function.Parameters[0].Type, out string? nativeReceiverName))
            return;

        managedKind = BindingManagedFunctionKind.Extension;
        receiverType = typeAnalyzer.Analyze(function.Parameters[0].Type).ManagedName;
        receiverIndex = 0;
        managedContainer = "Extensions";
        managedName = config.GetExtensionName(rawManagedName, config.GetExtensionNamePrefix(nativeReceiverName));
    }

    private static bool TryGetExtensionReceiverName(CppType type, [NotNullWhen(true)] out string? nativeName)
    {
        while (type is CppQualifiedType qualified)
            type = qualified.ElementType;
        if (type is CppTypedef typedef && typedef.IsOpaqueHandle())
        {
            nativeName = typedef.Name;
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
        if (function.Flags.HasFlag(CppFunctionFlags.Constructor))
            return BindingFunctionKind.Constructor;
        if (function.Flags.HasFlag(CppFunctionFlags.Destructor))
            return BindingFunctionKind.Destructor;
        if (function.IsCxxClassMethod && (function.StorageQualifier & CppStorageQualifier.Static) != 0)
            return BindingFunctionKind.Static;
        return function.Flags.HasFlag(CppFunctionFlags.Method)
            ? BindingFunctionKind.Instance
            : BindingFunctionKind.Free;
    }
}
