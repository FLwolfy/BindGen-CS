namespace BGCS.Analysis;

using System.Diagnostics.CodeAnalysis;
using BGCS.Core;
using BGCS.Core.CSharp;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
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
            GetLibraryExtensionFunctionName = config.GetLibraryExtensionFunctionName
        };
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
                    case CppClass cppClass when ShouldIncludeType(cppClass.Name):
                        module.Types.Add(layoutAnalyzer.Analyze(cppClass));
                        AnalyzeFieldDelegates(cppClass, module, delegateNames);
                        break;
                    case CppEnum cppEnum when ShouldIncludeEnum(cppEnum.Name):
                        BindingType enumType = new(cppEnum.FullName, config.GetCsCleanName(cppEnum.Name),
                            BindingTypeKind.Enumeration, cppEnum.IntegerType?.SizeOf ?? sizeof(int),
                            cppEnum.IntegerType?.SizeOf ?? sizeof(int))
                        {
                            UnderlyingType = cppEnum.IntegerType == null ? null : typeAnalyzer.Analyze(cppEnum.IntegerType)
                        };
                        EnumPrefix enumPrefix = config.GetEnumNamePrefix(cppEnum.Name);
                        foreach (CppEnumItem item in cppEnum.Items)
                            enumType.EnumMembers.Add(new(item.Name, config.GetEnumName(item.Name, enumPrefix), item.Value.ToString()));
                        module.Types.Add(enumType);
                        break;
                    case CppTypedef typedef when ShouldIncludeTypedef(typedef.Name):
                        if (TryGetDelegateType(typedef, out CppFunctionType? functionType))
                        {
                            AddDelegate(module, delegateNames, typedef.Name, config.GetDelegateName(typedef.Name), functionType);
                            break;
                        }
                        CppType ultimateType = GetUltimateType(typedef);
                        if (ultimateType is CppClass record &&
                            (!record.IsDefinition || !graph.TryGetNode(record, out _)))
                        {
                            string aliasName = config.GetCsCleanName(typedef.Name);
                            if (!opaqueRecordNames.TryGetValue(record, out string? canonicalName))
                            {
                                canonicalName = aliasName;
                                opaqueRecordNames.Add(record, canonicalName);
                                module.Types.Add(new(record.FullName, canonicalName, BindingTypeKind.Structure,
                                    Math.Max(0, record.SizeOf), Math.Max(1, record.AlignOf))
                                {
                                    IsOpaqueStorage = true
                                });
                            }
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
                            module.Types.Add(new(typedef.FullName, config.GetCsCleanName(typedef.Name),
                                BindingTypeKind.Structure, 0, 1)
                            {
                                IsOpaqueStorage = true
                            });
                            break;
                        }
                        module.Types.Add(new(typedef.FullName, config.GetCsCleanName(typedef.Name),
                            typedef.IsOpaqueHandle() ? BindingTypeKind.OpaqueHandle : BindingTypeKind.Alias,
                            typedef.SizeOf, GetTypeAlignment(ultimateType))
                        {
                            UnderlyingType = typedef.IsOpaqueHandle() ? null : typeAnalyzer.Analyze(ultimateType)
                        });
                        break;
                    case CppFunction function when ShouldIncludeFunction(function):
                        BindingFunction analyzedFunction = AnalyzeFunction(function);
                        if (functionTable != null)
                            analyzedFunction.FunctionTableIndex = functionTable.Add(function.Name);
                        module.Functions.Add(analyzedFunction);
                        break;
                }
            }
            catch (Exception exception)
            {
                module.Diagnostics.Add($"{node.Declaration}: {exception.Message}");
            }
        }
        if (functionTable != null)
        {
            foreach (BGCS.Metadata.CsFunctionTableEntry entry in functionTable.Entries.OrderBy(entry => entry.Index))
                module.FunctionTableEntries.Add(new(entry.Index, entry.EntryPoint));
        }
        AddSelfAliasStorageTypes(module);
        AnalyzeConstants(module, macros ?? []);
        new StrictSafetyAnalyzer(config).Analyze(module);
        return module;
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
                constants.TryAdd(macro.Name, constant);
        }
        foreach (BindingConstant constant in constants.Values)
            module.Constants.Add(constant);
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
        BindingTypeReference returnType = typeAnalyzer.Analyze(function.ReturnType);
        BindingFunction bindingFunction = new(function.Name, config.GetCsFunctionName(function.Name),
            GetFunctionKind(function), returnType, ownershipAnalyzer.Analyze(function.ReturnType, Direction.Out, marshallingMapping?.Return))
        {
            CallingConvention = function.CallingConvention.ToString(),
            IsVariadic = function.Flags.HasFlag(CppFunctionFlags.Variadic),
            DeclaringType = (function.Parent as CppClass)?.FullName
        };
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            CppParameter parameter = function.Parameters[i];
            Direction direction = parameter.Type.GetDirection();
            MarshallingMapping? parameterMapping = null;
            marshallingMapping?.Parameters.TryGetValue(parameter.Name, out parameterMapping);
            bindingFunction.Parameters.Add(new(parameter.Name, config.GetParameterName(i, parameter.Name),
                typeAnalyzer.Analyze(parameter.Type), ToBindingDirection(direction), ownershipAnalyzer.Analyze(parameter.Type, direction, parameterMapping)));
        }
        overloadPlanner.Plan(bindingFunction);
        return bindingFunction;
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
