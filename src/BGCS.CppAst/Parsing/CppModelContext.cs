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

/// <summary>
/// Defines the public class <c>CppModelContext</c>.
/// </summary>
public unsafe partial class CppModelContext
{
    private readonly Dictionary<CursorKey, CppContainerContext> containers;
    private readonly CppContainerContext userRootContainerContext;
    private readonly CppContainerContext systemRootContainerContext;
    private CppContainerContext rootContainerContext = null!;
    private readonly TypedefResolver typedefResolver = new();
    private readonly Dictionary<CursorKey, CppTemplateParameterType> objCTemplateParameterTypes;

    /// <summary>
    /// Initializes a new instance of <see cref="CppModelContext"/>.
    /// </summary>
    public CppModelContext(CppModelBuilder builder, CXTranslationUnit translationUnit)
    {
        Builder = builder;
        containers = [];
        RootCompilation = new(translationUnit);
        objCTemplateParameterTypes = [];
        userRootContainerContext = new(RootCompilation, CppContainerContextType.User, CppVisibility.Default);
        systemRootContainerContext = new(RootCompilation.System, CppContainerContextType.System, CppVisibility.Default);
    }

    /// <summary>
    /// Gets <c>RootCompilation</c>.
    /// </summary>
    public CppCompilation RootCompilation { get; }

    /// <summary>
    /// Exposes public member <c>CurrentRootContainer</c>.
    /// </summary>
    public CppContainerContext CurrentRootContainer
    {
        get => rootContainerContext;
        set => rootContainerContext = value;
    }

    /// <summary>
    /// Exposes public member <c>userRootContainerContext</c>.
    /// </summary>
    public CppContainerContext UserRootContainerContext => userRootContainerContext;

    /// <summary>
    /// Exposes public member <c>systemRootContainerContext</c>.
    /// </summary>
    public CppContainerContext SystemRootContainerContext => systemRootContainerContext;

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public CppGlobalDeclarationContainer GlobalDeclarationContainer => (CppGlobalDeclarationContainer)rootContainerContext.Container;

    /// <summary>
    /// Gets <c>Builder</c>.
    /// </summary>
    public CppModelBuilder Builder { get; }

    /// <summary>
    /// Exposes public member <c>typedefResolver</c>.
    /// </summary>
    public TypedefResolver TypedefResolver => typedefResolver;

    /// <summary>
    /// Exposes public member <c>objCTemplateParameterTypes</c>.
    /// </summary>
    public Dictionary<CursorKey, CppTemplateParameterType> ObjCTemplateParameterTypes => objCTemplateParameterTypes;

    /// <summary>
    /// Exposes public member <c>containers</c>.
    /// </summary>
    public Dictionary<CursorKey, CppContainerContext> Containers => containers;

    /// <summary>
    /// Gets or sets <c>CurrentClassBeingVisited</c>.
    /// </summary>
    public CppClass? CurrentClassBeingVisited { get; set; }

    /// <summary>
    /// Gets <c>MapTemplateParameterTypeToTypedefKeys</c>.
    /// </summary>
    public Dictionary<CppTemplateParameterType, HashSet<CursorKey>> MapTemplateParameterTypeToTypedefKeys { get; } = [];

    /// <summary>
    /// Gets or sets <c>CurrentTypedefKey</c>.
    /// </summary>
    public CursorKey CurrentTypedefKey { get; set; }

    /// <summary>
    /// Returns computed data from <c>GetOrCreateDeclContainer</c>.
    /// </summary>
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

    /// <summary>
    /// Returns computed data from <c>GetOrCreateDeclContainer</c>.
    /// </summary>
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
    /// <summary>
    /// Returns computed data from <c>GetCursorKey</c>.
    /// </summary>
    public CursorKey GetCursorKey(CXCursor cursor)
    {
        return new(rootContainerContext, cursor);
    }

    /// <summary>
    /// Executes public operation <c>TryToCreateTemplateParametersObjC</c>.
    /// </summary>
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
/// <summary>
/// Declares the callback signature <c>CXCursorBlockVisitor</c>.
/// </summary>
public unsafe delegate CXChildVisitResult CXCursorBlockVisitor(CXCursor cursor, CXCursor parent);
