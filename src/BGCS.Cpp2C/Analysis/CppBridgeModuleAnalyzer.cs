namespace BGCS.Cpp2C.Analysis;

using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.Cpp2C.Adapters;
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
            config.ResolvedTarget.Identifier);
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
                BindingDiagnosticCodes.CppInstantiation));
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
            IReadOnlyList<CppFunction> availableConstructors = cppClass.Constructors.Count > 0
                ? cppClass.Constructors.ToList()
                : cppClass.SpecializedTemplate?.Constructors.ToList() ?? [];
            List<CppFunction> constructors = availableConstructors.Where(constructor =>
                (constructor.Visibility is CppVisibility.Public or CppVisibility.Default) &&
                !constructor.Flags.HasFlag(CppFunctionFlags.Deleted)).ToList();
            if (!functions.Any(function => function.Flags.HasFlag(CppFunctionFlags.Pure)))
            {
                if (constructors.Count == 0 && availableConstructors.Count == 0)
                    module.Functions.Add(CreateImplicitLifecycle(cppClass, type.ManagedName + "Create", BindingFunctionKind.Constructor));
                for (int index = 0; index < constructors.Count; index++)
                {
                    CppFunction constructor = constructors[index];
                    string defaultName = type.ManagedName + "Create" + (index == 0 ? string.Empty : index.ToString());
                    if (!config.IsCallableExcluded(cppClass, constructor, defaultName))
                        module.Functions.Add(AnalyzeConstructor(cppClass, constructor, defaultName));
                }
            }
            CppFunction? destructor = cppClass.Destructors.FirstOrDefault(value =>
                (value.Visibility is CppVisibility.Public or CppVisibility.Default) &&
                !value.Flags.HasFlag(CppFunctionFlags.Deleted));
            string destroyName = type.ManagedName + "Destroy";
            if (cppClass.Destructors.Count == 0)
                module.Functions.Add(CreateImplicitLifecycle(cppClass, destroyName, BindingFunctionKind.Destructor));
            else if (destructor != null && !config.IsCallableExcluded(cppClass, destructor, destroyName))
                module.Functions.Add(AnalyzeDestructor(cppClass, destructor, destroyName));
            foreach (CppFunction function in functions)
            {
                string defaultName = $"{config.GetCTypeName(cppClass)}_{function.Name}";
                if (!config.IsCallableExcluded(cppClass, function, defaultName))
                    module.Functions.Add(AnalyzeFunction(cppClass, function, defaultName));
            }
        }
        foreach (CppFunction function in container.Functions.Where(function => function.TemplateParameters.Count == 0))
        {
            string defaultName = config.GetCFunctionName(function);
            if (!config.IsCallableExcluded(null, function, defaultName))
                module.Functions.Add(AnalyzeFunction(null, function, defaultName));
        }
        foreach (CppNamespace cppNamespace in container.Namespaces)
            AnalyzeContainer(cppNamespace, module);
    }

    private BindingFunction AnalyzeFunction(CppClass? declaringType, CppFunction function, string defaultName)
    {
        BindingFunctionKind kind = function.Flags.HasFlag(CppFunctionFlags.Constructor)
            ? BindingFunctionKind.Constructor
            : function.Flags.HasFlag(CppFunctionFlags.Destructor)
                ? BindingFunctionKind.Destructor
                : declaringType == null ? BindingFunctionKind.Free
                : (function.StorageQualifier & CppStorageQualifier.Static) != 0 ? BindingFunctionKind.Static
                : BindingFunctionKind.Instance;
        BindingFunction result = new(function.Name, config.GetCFunctionName(declaringType, function, defaultName), kind,
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

    private BindingFunction AnalyzeConstructor(CppClass declaringType, CppFunction constructor, string defaultName)
    {
        string typeName = config.GetCTypeName(declaringType);
        BindingFunction result = new(constructor.Name, config.GetCFunctionName(declaringType, constructor, defaultName),
            BindingFunctionKind.Constructor, new(declaringType.FullName + "*", typeName + "*", 1, false, nint.Size),
            new(MarshallingStrategy.Handle, BindingOwnership.Owned))
        {
            DeclaringType = declaringType.FullName,
            CallingConvention = constructor.CallingConvention.ToString()
        };
        foreach (CppParameter parameter in constructor.Parameters)
            result.Parameters.Add(new(parameter.Name, parameter.Name, AnalyzeType(declaringType, parameter.Type), BindingDirection.In,
                AnalyzeMarshalling(parameter.Type, parameter.Name)));
        return result;
    }

    private BindingFunction AnalyzeDestructor(CppClass declaringType, CppFunction destructor, string defaultName)
    {
        BindingFunction result = CreateImplicitLifecycle(declaringType,
            config.GetCFunctionName(declaringType, destructor, defaultName), BindingFunctionKind.Destructor);
        return result;
    }

    private BindingFunction CreateImplicitLifecycle(CppClass declaringType, string exportedName, BindingFunctionKind kind)
    {
        BindingTypeReference returnType = kind == BindingFunctionKind.Constructor
            ? new(declaringType.FullName + "*", config.GetCTypeName(declaringType) + "*", 1, false, nint.Size)
            : new("void", "void", 0, false, 0);
        BindingFunction result = new(kind == BindingFunctionKind.Constructor ? declaringType.Name : "~" + declaringType.Name,
            exportedName, kind, returnType,
            new(kind == BindingFunctionKind.Constructor ? MarshallingStrategy.Handle : MarshallingStrategy.Blittable,
                kind == BindingFunctionKind.Constructor ? BindingOwnership.Owned : BindingOwnership.Borrowed))
        {
            DeclaringType = declaringType.FullName
        };
        if (kind == BindingFunctionKind.Destructor)
            result.Parameters.Add(new("self", "self", new(declaringType.FullName + "*", config.GetCTypeName(declaringType) + "*", 1, false, nint.Size),
                BindingDirection.In, new(MarshallingStrategy.Handle, BindingOwnership.Transferred)));
        return result;
    }

    private MarshallingPlan AnalyzeMarshalling(CppType type, string? parameterName)
    {
        CppTypeAdapterPlan? adapter = config.ResolveTypeAdapter(type,
            parameterName == null ? CppTypeAdapterUse.Return : CppTypeAdapterUse.Parameter);
        if (adapter != null)
        {
            string? length = adapter.Kind is CppTypeAdapterKind.Span or CppTypeAdapterKind.Vector
                ? parameterName == null ? "out_count" : parameterName + "_count"
                : null;
            return new(adapter.Marshalling, adapter.Ownership,
                adapter.Kind == CppTypeAdapterKind.Utf8String ? BindingStringEncoding.Utf8 : BindingStringEncoding.None,
                NullTerminated: adapter.Kind == CppTypeAdapterKind.Utf8String,
                LengthParameter: length,
                RequiresCleanup: adapter.RequiresCleanup);
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
