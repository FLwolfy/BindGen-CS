using System;
using System.Collections.Generic;

namespace BGCS.CppAst.Parsing;

using System.Runtime.CompilerServices;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CppModelContext</c>.
/// </summary>
internal unsafe partial class CppModelContext
{
    private readonly Dictionary<CursorKey, CppContainerContext> m_containers;
    internal DeclContainerVisitorRegistry declVisitors { get; } = new();
    internal MemberVisitorRegistry memberVisitors { get; } = new();
    private readonly CppContainerContext m_userRootContainerContext;
    private readonly CppContainerContext m_systemRootContainerContext;
    private CppContainerContext m_rootContainerContext = null!;
    private readonly TypedefResolver m_typedefResolver = new();
    private readonly Dictionary<CursorKey, CppTemplateParameterType> m_objCTemplateParameterTypes;
    private readonly Dictionary<(ResolverScope Scope, uint Hash, CXCursorKind Kind), List<CursorKey>> m_cursorKeys;
    /// <summary>
    /// Initializes a new instance of <see cref = "CppModelContext"/>.
    /// </summary>
    public CppModelContext(
        CppModelBuilder builder,
        CXTranslationUnit translationUnit
    ) {
        this.builder = builder;
        this.m_containers = [];
        this.rootCompilation = new(translationUnit);
        this.m_objCTemplateParameterTypes = [];
        this.m_cursorKeys = [];
        this.m_userRootContainerContext = new(this.rootCompilation, CppContainerContextType.User, CppVisibility.Default);
        this.m_systemRootContainerContext = new(this.rootCompilation.system, CppContainerContextType.System, CppVisibility.Default);
    }

    /// <summary>
    /// Gets <c>RootCompilation</c>.
    /// </summary>
    public CppCompilation rootCompilation { get; }
    /// <summary>
    /// Exposes public member <c>CurrentRootContainer</c>.
    /// </summary>
    public CppContainerContext currentRootContainer { get => this.m_rootContainerContext; set => this.m_rootContainerContext = value; }
    /// <summary>
    /// Exposes public member <c>userRootContainerContext</c>.
    /// </summary>
    public CppContainerContext userRootContainerContext => this.m_userRootContainerContext;
    /// <summary>
    /// Exposes public member <c>systemRootContainerContext</c>.
    /// </summary>
    public CppContainerContext systemRootContainerContext => this.m_systemRootContainerContext;
    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public CppGlobalDeclarationContainer globalDeclarationContainer => (CppGlobalDeclarationContainer)this.m_rootContainerContext.container;
    /// <summary>
    /// Gets <c>Builder</c>.
    /// </summary>
    public CppModelBuilder builder { get; }
    /// <summary>
    /// Exposes public member <c>typedefResolver</c>.
    /// </summary>
    public TypedefResolver typedefResolver => this.m_typedefResolver;
    /// <summary>
    /// Exposes public member <c>objCTemplateParameterTypes</c>.
    /// </summary>
    public Dictionary<CursorKey, CppTemplateParameterType> objCTemplateParameterTypes => this.m_objCTemplateParameterTypes;
    /// <summary>
    /// Exposes public member <c>containers</c>.
    /// </summary>
    public Dictionary<CursorKey, CppContainerContext> containers => this.m_containers;
    /// <summary>
    /// Gets or sets <c>CurrentClassBeingVisited</c>.
    /// </summary>
    public CppClass? currentClassBeingVisited { get; set; }
    /// <summary>
    /// Gets <c>MapTemplateParameterTypeToTypedefKeys</c>.
    /// </summary>
    public Dictionary<CppTemplateParameterType, HashSet<CursorKey>> mapTemplateParameterTypeToTypedefKeys { get; } = [];
    /// <summary>
    /// Gets or sets <c>CurrentTypedefKey</c>.
    /// </summary>
    public CursorKey currentTypedefKey { get; set; }

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
        if (this.containers.TryGetValue(typeKey, out var containerContext))
        {
            return containerContext;
        }

        var visitor = declVisitors.GetVisitor(cursor.Kind);
        containerContext = visitor.Visit(this, cursor, cursor.SemanticParent);
        this.containers.TryAdd(typeKey, containerContext);
        return containerContext;
    }

    /// <summary>
    /// Returns computed data from <c>GetOrCreateDeclContainer</c>.
    /// </summary>
    public TCppElement GetOrCreateDeclContainer<TCppElement>(
        CXCursor cursor,
        out CppContainerContext context
    )
        where TCppElement : CppElement, ICppContainer
    {
        context = GetOrCreateDeclContainer(cursor);
        if (context.container is TCppElement typedCppElement)
        {
            return typedCppElement;
        }

        throw new InvalidOperationException($"The element `{context.container}` doesn't match the expected type `{typeof(TCppElement)}");
    }

    /// <summary>
    /// Returns computed data from <c>GetCursorKey</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CursorKey GetCursorKey(CXCursor cursor)
    {
        while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
        {
            cursor = cursor.SemanticParent;
        }

        ResolverScope scope = this.m_rootContainerContext.type == CppContainerContextType.User ? ResolverScope.User : ResolverScope.System;
        var cacheKey = (scope, cursor.Hash, cursor.Kind);
        if (this.m_cursorKeys.TryGetValue(cacheKey, out List<CursorKey>? bucket))
        {
            for (int i = 0; i < bucket.Count; i++)
            {
                if (clang.equalCursors(bucket[i].cursor, cursor) != 0)
                {
                    return bucket[i];
                }
            }
        }
        else
        {
            bucket = [];
            this.m_cursorKeys.Add(cacheKey, bucket);
        }

        CursorKey key = new(this.m_rootContainerContext, cursor);
        bucket.Add(key);
        return key;
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
        if (!this.m_objCTemplateParameterTypes.TryGetValue(key, out var templateParameterType))
        {
            var templateParameterName = CXUtil.GetCursorSpelling(cursor);
            templateParameterType = new(cursor, templateParameterName);
            this.m_objCTemplateParameterTypes.Add(key, templateParameterType);
        }

        return templateParameterType;
    }
}
