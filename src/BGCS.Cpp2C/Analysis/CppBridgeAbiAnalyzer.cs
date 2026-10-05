using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.IO;
using BGCS.Cpp2C.Configuration;

namespace BGCS.Cpp2C.Analysis;

using BGCS.Cpp2C.Lowering;
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
internal sealed class CppBridgeAbiAnalyzer
{
    private readonly Cpp2CGeneratorConfig m_config;
    private int m_pointerSize;
    internal CppBridgeAbiAnalyzer(Cpp2CGeneratorConfig config)
    {
        m_config = config;
    }

    internal BindingModule Analyze(
        BGCS.CppAst.Model.Metadata.CppCompilation compilation,
        FileSet files
    ) {
        m_pointerSize = compilation.pointerSize;
        List<BindingType> types = [];
        List<BindingFunction> functions = [];
        List<BindingDiagnostic> diagnostics = [];
        AnalyzeContainer(compilation, files, types, functions, diagnostics);
        foreach (string lowering in m_config.lowerings.unsafeBypasses)
            diagnostics.Add(new(BindingDiagnosticSeverity.Warning, $"Unsafe lowering '{lowering}' was explicitly allowed by LoweringSafetyPolicy=AllowUnsafe. The generated ABI must be covered by project-owned compile, invocation, and lifetime tests.", BindingDiagnosticCodes.C_UNSAFELOWERING));
        return new("CppBridge", "C", string.Empty, m_config.resolvedTarget.targetId.value)
        {
            types = types,
            functions = functions,
            structuredDiagnostics = diagnostics
        };
    }

    private void AnalyzeContainer(
        ICppGlobalDeclarationContainer container,
        FileSet files,
        List<BindingType> types,
        List<BindingFunction> functions,
        List<BindingDiagnostic> diagnostics
    ) {
        CppClass[] classes = CppBridgeDeclarationPolicy.EnumerateAccessibleClasses(container.classes)
            .Where(value => files.Contains(value.sourceFile)).ToArray();
        foreach (CppClass template in classes.Where(value => value.templateKind == CppTemplateKind.TemplateClass))
        {
            string templatePrefix = template.fullName.Split('<')[0] + "<";
            if (m_config.templateInstantiations.Any(value => value.StartsWith(templatePrefix, StringComparison.Ordinal)))
                continue;
            diagnostics.Add(new(BindingDiagnosticSeverity.Warning, $"Primary template '{template.fullName}' is not emitted. Add only the required concrete specialization to TemplateInstantiations.", BindingDiagnosticCodes.C_CPPINSTANTIATION));
        }

        IEnumerable<CppEnum> enums = container.enums.Concat(classes.SelectMany(static value => value.enums));
        foreach (CppEnum cppEnum in enums.Where(value => files.Contains(value.sourceFile)
            && CppBridgeDeclarationPolicy.IsAccessible(value)))
            types.Add(new(cppEnum.fullName, m_config.GetCTypeName(cppEnum), BindingTypeKind.Enumeration, cppEnum.integerType?.sizeOf ?? sizeof(int), cppEnum.integerType?.sizeOf ?? sizeof(int)));
        foreach (CppClass cppClass in classes.Where(cppClass => cppClass.templateKind != CppTemplateKind.TemplateClass
            && cppClass.isDefinition && (cppClass.classKind is CppClassKind.Class or CppClassKind.Struct)
            && !IsLoweredType(cppClass)))
        {
            IReadOnlyList<CppFunction> memberFunctions = cppClass.functions.Count > 0 ? cppClass.functions.ToList() : cppClass.specializedTemplate?.functions.ToList() ?? [];
            bool opaque = CppBridgeDeclarationPolicy.RequiresOpaqueBridge(cppClass);
            BindingType type = new(cppClass.fullName, m_config.GetCTypeName(cppClass),
                opaque ? BindingTypeKind.OpaqueHandle : BindingTypeKind.Structure, cppClass.sizeOf, cppClass.alignOf);
            types.Add(type);
            if (!opaque)
                continue;
            IReadOnlyList<CppFunction> availableConstructors = cppClass.constructors.Count > 0 ? cppClass.constructors.ToList() : cppClass.specializedTemplate?.constructors.ToList() ?? [];
            List<CppFunction> constructors = availableConstructors.Where(constructor => (constructor.visibility is CppVisibility.Public or CppVisibility.Default) && !constructor.flags.HasFlag(CppFunctionFlags.Deleted)).ToList();
            if (!memberFunctions.Any(function => function.flags.HasFlag(CppFunctionFlags.Pure)))
            {
                if (constructors.Count == 0 && availableConstructors.Count == 0)
                    functions.Add(CreateImplicitLifecycle(cppClass, type.managedName + "Create", BindingFunctionKind.Constructor));
                for (int index = 0; index < constructors.Count; index++)
                {
                    CppFunction constructor = constructors[index];
                    string defaultName = type.managedName + "Create" + (index == 0 ? string.Empty : index.ToString());
                    if (!m_config.IsCallableExcluded(cppClass, constructor, defaultName))
                        functions.Add(AnalyzeConstructor(cppClass, constructor, defaultName));
                }
            }

            CppFunction? destructor = cppClass.destructors.FirstOrDefault(value => (value.visibility is CppVisibility.Public or CppVisibility.Default) && !value.flags.HasFlag(CppFunctionFlags.Deleted));
            string destroyName = type.managedName + "Destroy";
            if (cppClass.destructors.Count == 0)
                functions.Add(CreateImplicitLifecycle(cppClass, destroyName, BindingFunctionKind.Destructor));
            else if (destructor != null && !m_config.IsCallableExcluded(cppClass, destructor, destroyName))
                functions.Add(AnalyzeDestructor(cppClass, destructor, destroyName));
            foreach (CppFunction function in memberFunctions)
            {
                string defaultName = $"{m_config.GetCTypeName(cppClass)}_{function.name}";
                if (CppBridgeDeclarationPolicy.IsAccessible(function)
                    && !function.flags.HasFlag(CppFunctionFlags.Deleted)
                    && !m_config.IsCallableExcluded(cppClass, function, defaultName))
                    functions.Add(AnalyzeFunction(cppClass, function, defaultName));
            }
        }

        foreach (CppFunction function in container.functions.Where(function => files.Contains(function.sourceFile)
            && function.templateParameters.Count == 0 && !function.isExternC
            && CppBridgeDeclarationPolicy.IsAccessible(function)))
        {
            string defaultName = m_config.GetCFunctionName(function);
            if (!m_config.IsCallableExcluded(null, function, defaultName))
                functions.Add(AnalyzeFunction(null, function, defaultName));
        }

        foreach (CppNamespace cppNamespace in container.namespaces)
            AnalyzeContainer(cppNamespace, files, types, functions, diagnostics);
    }

    private BindingFunction AnalyzeFunction(
        CppClass? declaringType,
        CppFunction function,
        string defaultName
    ) {
        BindingFunctionKind kind = function.flags.HasFlag(CppFunctionFlags.Constructor) ? BindingFunctionKind.Constructor : function.flags.HasFlag(CppFunctionFlags.Destructor) ? BindingFunctionKind.Destructor : declaringType == null ? BindingFunctionKind.Free : (function.storageQualifier & CppStorageQualifier.Static) != 0 ? BindingFunctionKind.Static : BindingFunctionKind.Instance;
        BindingFunction result = new(function.name, m_config.GetCFunctionName(declaringType, function, defaultName), kind, AnalyzeType(declaringType, function.returnType), AnalyzeMarshalling(function.returnType, null))
        {
            callingConvention = function.callingConvention.ToString(),
            isVariadic = function.flags.HasFlag(CppFunctionFlags.Variadic),
            declaringType = declaringType?.fullName
        };
        foreach (CppParameter parameter in function.parameters)
            result = result with
            {
                parameters = [.. result.parameters, new(parameter.name, parameter.name, AnalyzeType(declaringType, parameter.type), BindingDirection.In, AnalyzeMarshalling(parameter.type, parameter.name))]
            };
        return result;
    }

    private BindingFunction AnalyzeConstructor(
        CppClass declaringType,
        CppFunction constructor,
        string defaultName
    ) {
        string typeName = m_config.GetCTypeName(declaringType);
        BindingFunction result = new(constructor.name, m_config.GetCFunctionName(declaringType, constructor, defaultName), BindingFunctionKind.Constructor, new(declaringType.fullName + "*", typeName + "*", 1, false, m_pointerSize), new(MarshallingStrategy.Handle, BindingOwnership.Owned))
        {
            declaringType = declaringType.fullName,
            callingConvention = constructor.callingConvention.ToString()
        };
        foreach (CppParameter parameter in constructor.parameters)
            result = result with
            {
                parameters = [.. result.parameters, new(parameter.name, parameter.name, AnalyzeType(declaringType, parameter.type), BindingDirection.In, AnalyzeMarshalling(parameter.type, parameter.name))]
            };
        return result;
    }

    private BindingFunction AnalyzeDestructor(
        CppClass declaringType,
        CppFunction destructor,
        string defaultName
    ) {
        BindingFunction result = CreateImplicitLifecycle(declaringType, m_config.GetCFunctionName(declaringType, destructor, defaultName), BindingFunctionKind.Destructor);
        return result;
    }

    private BindingFunction CreateImplicitLifecycle(
        CppClass declaringType,
        string exportedName,
        BindingFunctionKind kind
    ) {
        BindingTypeReference returnType = kind == BindingFunctionKind.Constructor ? new(declaringType.fullName + "*", m_config.GetCTypeName(declaringType) + "*", 1, false, m_pointerSize) : new("void", "void", 0, false, 0);
        BindingFunction result = new(kind == BindingFunctionKind.Constructor ? declaringType.name : "~" + declaringType.name, exportedName, kind, returnType, new(kind == BindingFunctionKind.Constructor ? MarshallingStrategy.Handle : MarshallingStrategy.Blittable, kind == BindingFunctionKind.Constructor ? BindingOwnership.Owned : BindingOwnership.Borrowed))
        {
            declaringType = declaringType.fullName
        };
        if (kind == BindingFunctionKind.Destructor)
            result = result with
            {
                parameters = [.. result.parameters, new("self", "self", new(declaringType.fullName + "*", m_config.GetCTypeName(declaringType) + "*", 1, false, m_pointerSize), BindingDirection.In, new(MarshallingStrategy.Handle, BindingOwnership.Transferred))]
            };
        return result;
    }

    private MarshallingPlan AnalyzeMarshalling(
        CppType type,
        string? parameterName
    ) {
        CppTypeLoweringPlan? lowering = m_config.ResolveTypeLowering(type, parameterName == null ? CppTypeLoweringUse.Return : CppTypeLoweringUse.Parameter);
        if (lowering != null)
        {
            string? length = lowering.kind is CppTypeLoweringKind.Span or CppTypeLoweringKind.Vector or CppTypeLoweringKind.Array ? parameterName == null ? "out_count" : parameterName + "_count" : null;
            return new(lowering.marshalling, lowering.ownership, lowering.kind is CppTypeLoweringKind.Utf8String or CppTypeLoweringKind.Path ? BindingStringEncoding.Utf8 : BindingStringEncoding.None, lengthParameter: length, requiresCleanup: lowering.requiresCleanup, cleanupFunction: lowering.cleanupFunction, nullTerminated: lowering.kind is CppTypeLoweringKind.Utf8String or CppTypeLoweringKind.Path, allocatorKind: lowering.allocatorKind, allocatorFunction: lowering.allocatorFunction);
        }

        return new(MarshallingStrategy.Blittable, BindingOwnership.Borrowed);
    }

    private bool IsLoweredType(CppType type) => m_config.ResolveTypeLowering(type, CppTypeLoweringUse.Field) != null;
    private BindingTypeReference AnalyzeType(
        CppClass? declaringType,
        CppType type
    ) {
        CppType resolvedType = ResolveTemplateType(declaringType, type);
        int pointerDepth = 0;
        CppType current = resolvedType;
        while (current is CppTypeWithElementType wrapper)
        {
            if (current is CppPointerType or CppReferenceType)
                pointerDepth++;
            current = wrapper.elementType;
        }

        return new(type.GetDisplayName(), m_config.GetCType(resolvedType), pointerDepth, false, resolvedType.sizeOf);
    }

    private static CppType ResolveTemplateType(
        CppClass? declaringType,
        CppType type
    ) {
        if (declaringType?.specializedTemplate == null)
            return type;
        string? parameterName = type switch
        {
            CppTemplateParameterType parameter => parameter.name,
            CppUnexposedType unexposed => unexposed.name,
            _ => null
        };
        if (parameterName == null)
            return type;
        int index = declaringType.specializedTemplate.templateParameters.ToList().FindIndex(candidate => candidate is CppTemplateParameterType parameter && parameter.name == parameterName);
        return index >= 0 && index < declaringType.templateSpecializedArguments.Count && declaringType.templateSpecializedArguments[index].argAsType is CppType argumentType ? argumentType : type;
    }
}
