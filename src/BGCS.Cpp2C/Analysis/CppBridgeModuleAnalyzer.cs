namespace BGCS.Cpp2C.Analysis;

using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.Intermediate;

/// <summary>
/// Lowers C++ classes, enums, and methods into the shared binding intermediate representation.
/// </summary>
internal sealed class CppBridgeModuleAnalyzer
{
    private readonly Cpp2CGeneratorConfig config;

    internal CppBridgeModuleAnalyzer(Cpp2CGeneratorConfig config)
    {
        this.config = config;
    }

    internal BindingModule Analyze(BGCS.CppAst.Model.Metadata.CppCompilation compilation)
    {
        BindingModule module = new("CppBridge", "C", string.Empty,
            $"windows-{config.TargetCpu.ToString().ToLowerInvariant()}-msvc");
        AnalyzeContainer(compilation, module);
        return module;
    }

    private void AnalyzeContainer(ICppGlobalDeclarationContainer container, BindingModule module)
    {
        foreach (CppClass template in container.Classes.Where(value => value.TemplateKind == CppTemplateKind.TemplateClass))
        {
            string templatePrefix = template.FullName.Split('<')[0] + "<";
            if (config.TemplateInstantiations.Any(value => value.StartsWith(templatePrefix, StringComparison.Ordinal)))
                continue;
            module.StructuredDiagnostics.Add(new(BindingDiagnosticSeverity.Warning,
                $"Primary template '{template.FullName}' is not emitted. Add only the required concrete specialization to TemplateInstantiations.",
                "BGCSCPP-INSTANTIATION"));
        }
        foreach (CppEnum cppEnum in container.Enums)
            module.Types.Add(new(cppEnum.FullName, config.GetCTypeName(cppEnum), BindingTypeKind.Enumeration,
                cppEnum.IntegerType?.SizeOf ?? sizeof(int), cppEnum.IntegerType?.SizeOf ?? sizeof(int)));
        foreach (CppClass cppClass in container.Classes.Where(cppClass => cppClass.SourceFile != null && cppClass.TemplateKind != CppTemplateKind.TemplateClass && !config.IsUtf8StringType(cppClass) && !config.IsSpanType(cppClass) && !config.IsVectorType(cppClass) && !config.IsUniquePtrType(cppClass) && !config.IsSharedPtrType(cppClass) && !config.IsOptionalType(cppClass)))
        {
            IReadOnlyList<CppFunction> functions = cppClass.Functions.Count > 0
                ? cppClass.Functions.ToList()
                : cppClass.SpecializedTemplate?.Functions.ToList() ?? [];
            BindingType type = new(cppClass.FullName, config.GetCTypeName(cppClass),
                cppClass.ClassKind == CppClassKind.Class || functions.Count > 0
                    ? BindingTypeKind.OpaqueHandle
                    : cppClass.ClassKind == CppClassKind.Union ? BindingTypeKind.Union : BindingTypeKind.Structure,
                cppClass.SizeOf, cppClass.AlignOf);
            module.Types.Add(type);
            foreach (CppFunction function in functions)
                module.Functions.Add(AnalyzeFunction(cppClass, function));
        }
        foreach (CppFunction function in container.Functions.Where(function => function.TemplateParameters.Count == 0))
            module.Functions.Add(AnalyzeFunction(null, function));
        foreach (CppNamespace cppNamespace in container.Namespaces)
            AnalyzeContainer(cppNamespace, module);
    }

    private BindingFunction AnalyzeFunction(CppClass? declaringType, CppFunction function)
    {
        BindingFunctionKind kind = function.Flags.HasFlag(CppFunctionFlags.Constructor)
            ? BindingFunctionKind.Constructor
            : function.Flags.HasFlag(CppFunctionFlags.Destructor)
                ? BindingFunctionKind.Destructor
                : declaringType == null ? BindingFunctionKind.Free : BindingFunctionKind.Instance;
        BindingFunction result = new(function.Name, config.NamePrefix + function.Name, kind,
            AnalyzeType(declaringType, function.ReturnType), AnalyzeMarshalling(function.ReturnType, null))
        {
            CallingConvention = function.CallingConvention.ToString(),
            IsVariadic = function.Flags.HasFlag(CppFunctionFlags.Variadic),
            DeclaringType = declaringType?.FullName
        };
        foreach (CppParameter parameter in function.Parameters)
            result.Parameters.Add(new(parameter.Name, parameter.Name, AnalyzeType(declaringType, parameter.Type), BindingDirection.In,
                AnalyzeMarshalling(parameter.Type, parameter.Name)));
        return result;
    }

    private MarshallingPlan AnalyzeMarshalling(CppType type, string? parameterName)
    {
        if (config.IsUtf8StringType(type))
            return new(MarshallingStrategy.String, BindingOwnership.Borrowed, BindingStringEncoding.Utf8, NullTerminated: true);
        if (config.IsUniquePtrType(type))
            return new(MarshallingStrategy.Pointer, BindingOwnership.Transferred, RequiresCleanup: true);
        if (config.IsSharedPtrType(type))
            return new(MarshallingStrategy.Pointer, BindingOwnership.Shared);
        if (config.IsSpanType(type) || config.IsVectorType(type))
            return new(MarshallingStrategy.Span, BindingOwnership.Borrowed,
                LengthParameter: parameterName == null ? "out_count" : parameterName + "_count");
        if (config.IsOptionalType(type))
        {
            bool nonBlittable = config.TryGetTemplateElementType(type, out CppType? elementType) &&
                !config.IsBlittableBridgeType(elementType!);
            return new(MarshallingStrategy.Optional,
                nonBlittable ? BindingOwnership.Owned : BindingOwnership.Borrowed,
                RequiresCleanup: nonBlittable);
        }
        return new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private BindingTypeReference AnalyzeType(CppClass? declaringType, CppType type)
    {
        CppType resolvedType = ResolveTemplateType(declaringType, type);
        int pointerDepth = 0;
        CppType current = resolvedType;
        while (current is CppTypeWithElementType wrapper)
        {
            if (current is CppPointerType or CppReferenceType)
                pointerDepth++;
            current = wrapper.ElementType;
        }
        return new(type.GetDisplayName(), config.GetCType(resolvedType), pointerDepth, false, resolvedType.SizeOf);
    }

    private static CppType ResolveTemplateType(CppClass? declaringType, CppType type)
    {
        if (declaringType?.SpecializedTemplate == null)
            return type;
        string? parameterName = type switch
        {
            CppTemplateParameterType parameter => parameter.Name,
            CppUnexposedType unexposed => unexposed.Name,
            _ => null
        };
        if (parameterName == null)
            return type;
        int index = declaringType.SpecializedTemplate.TemplateParameters.ToList()
            .FindIndex(candidate => candidate is CppTemplateParameterType parameter && parameter.Name == parameterName);
        return index >= 0 && index < declaringType.TemplateSpecializedArguments.Count &&
            declaringType.TemplateSpecializedArguments[index].ArgAsType is CppType argumentType
                ? argumentType
                : type;
    }
}
