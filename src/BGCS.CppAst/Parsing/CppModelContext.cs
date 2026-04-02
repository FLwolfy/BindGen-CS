using System;
using System.Collections.Generic;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Utilities;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public unsafe partial class CppModelContext
{
    private readonly Dictionary<CursorKey, CppContainerContext> containers;
    private readonly CppContainerContext userRootContainerContext;
    private readonly CppContainerContext systemRootContainerContext;
    private CppContainerContext rootContainerContext = null!;
    private readonly TypedefResolver typedefResolver = new();
    private readonly Dictionary<CursorKey, CppTemplateParameterType> objCTemplateParameterTypes;

    public CppModelContext(CppModelBuilder builder, CXTranslationUnit translationUnit)
    {
        Builder = builder;
        containers = [];
        RootCompilation = new(translationUnit);
        objCTemplateParameterTypes = [];
        userRootContainerContext = new(RootCompilation, CppContainerContextType.User, CppVisibility.Default);
        systemRootContainerContext = new(RootCompilation.System, CppContainerContextType.System, CppVisibility.Default);
    }

    public CppCompilation RootCompilation { get; }

    public CppContainerContext CurrentRootContainer
    {
        get => rootContainerContext;
        set => rootContainerContext = value;
    }

    public CppContainerContext UserRootContainerContext => userRootContainerContext;

    public CppContainerContext SystemRootContainerContext => systemRootContainerContext;

    public CppGlobalDeclarationContainer GlobalDeclarationContainer => (CppGlobalDeclarationContainer)rootContainerContext.Container;

    public CppModelBuilder Builder { get; }

    public TypedefResolver TypedefResolver => typedefResolver;

    public Dictionary<CursorKey, CppTemplateParameterType> ObjCTemplateParameterTypes => objCTemplateParameterTypes;

    public Dictionary<CursorKey, CppContainerContext> Containers => containers;

    public CppClass? CurrentClassBeingVisited { get; set; }

    public Dictionary<CppTemplateParameterType, HashSet<CursorKey>> MapTemplateParameterTypeToTypedefKeys { get; } = [];

    public CursorKey CurrentTypedefKey { get; set; }

    public CppContainerContext GetOrCreateDeclContainer(CXCursor cursor)
    {
        while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
        {
            cursor = cursor.SemanticParent;
        }

        var typeKey = GetCursorKey(cursor);
        if (Containers.TryGetValue(typeKey, out var containerContext))
        {
            return containerContext;
        }

        var visitor = DeclContainerVisitorRegistry.GetVisitor(cursor.Kind);
        containerContext = visitor.Visit(this, cursor, cursor.SemanticParent);
        Containers.TryAdd(typeKey, containerContext);
        return containerContext;
    }

    public TCppElement GetOrCreateDeclContainer<TCppElement>(CXCursor cursor, out CppContainerContext context) where TCppElement : CppElement, ICppContainer
    {
        context = GetOrCreateDeclContainer(cursor);
        if (context.Container is TCppElement typedCppElement)
        {
            return typedCppElement;
        }
        throw new InvalidOperationException($"The element `{context.Container}` doesn't match the expected type `{typeof(TCppElement)}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CursorKey GetCursorKey(CXCursor cursor)
    {
        return new(rootContainerContext, cursor);
    }

    public CppTemplateParameterType? TryToCreateTemplateParametersObjC(CXCursor cursor)
    {
        if (cursor.Kind != CXCursorKind.CXCursor_TemplateTypeParameter)
        {
            return null;
        }

        var key = GetCursorKey(cursor);
        if (!objCTemplateParameterTypes.TryGetValue(key, out var templateParameterType))
        {
            var templateParameterName = CXUtil.GetCursorSpelling(cursor);
            templateParameterType = new(cursor, templateParameterName);
            objCTemplateParameterTypes.Add(key, templateParameterType);
        }
        return templateParameterType;
    }
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate CXChildVisitResult CXCursorBlockVisitor(CXCursor cursor, CXCursor parent);
