namespace BGCS.Analysis;

using BGCS.Core;
using BGCS.Core.CSharp;
using BGCS.CppAst.Model.Declarations;
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

    public BindingModule Analyze(DeclarationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        BindingModule module = new(config.ApiName, config.Namespace, config.LibName,
            config.ResolvedTarget.Identifier);
        foreach (DeclarationGraphNode node in graph.TopologicalOrder())
        {
            try
            {
                switch (node.Declaration)
                {
                    case CppClass cppClass when !string.IsNullOrWhiteSpace(cppClass.Name):
                        module.Types.Add(layoutAnalyzer.Analyze(cppClass));
                        break;
                    case CppEnum cppEnum when !string.IsNullOrWhiteSpace(cppEnum.Name):
                        BindingType enumType = new(cppEnum.FullName, config.GetCsCleanName(cppEnum.Name),
                            BindingTypeKind.Enumeration, cppEnum.IntegerType?.SizeOf ?? sizeof(int),
                            cppEnum.IntegerType?.SizeOf ?? sizeof(int));
                        EnumPrefix enumPrefix = config.GetEnumNamePrefix(cppEnum.Name);
                        foreach (CppEnumItem item in cppEnum.Items)
                            enumType.EnumMembers.Add(new(item.Name, config.GetEnumName(item.Name, enumPrefix), item.Value.ToString()));
                        module.Types.Add(enumType);
                        break;
                    case CppTypedef typedef when !string.IsNullOrWhiteSpace(typedef.Name):
                        module.Types.Add(new(typedef.FullName, config.GetCsCleanName(typedef.Name),
                            typedef.IsOpaqueHandle() ? BindingTypeKind.OpaqueHandle : BindingTypeKind.Alias,
                            typedef.SizeOf, Math.Max(1, typedef.SizeOf)));
                        break;
                    case CppFunction function:
                        module.Functions.Add(AnalyzeFunction(function));
                        break;
                }
            }
            catch (Exception exception)
            {
                module.Diagnostics.Add($"{node.Declaration}: {exception.Message}");
            }
        }
        new StrictSafetyAnalyzer(config).Analyze(module);
        return module;
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
        if ((function.StorageQualifier & CppStorageQualifier.Static) != 0)
            return BindingFunctionKind.Static;
        return function.Flags.HasFlag(CppFunctionFlags.Method)
            ? BindingFunctionKind.Instance
            : BindingFunctionKind.Free;
    }
}
