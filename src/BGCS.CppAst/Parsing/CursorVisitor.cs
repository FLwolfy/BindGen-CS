using System;
using System.Collections.Generic;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Defines the public class <c>CursorVisitor</c>.
/// </summary>
public abstract class CursorVisitor<TResult>
{
    /// <summary>
    /// Gets or sets <c>Context</c>.
    /// </summary>
    public CppModelContext Context { get; internal set; } = null!;

    /// <summary>
    /// Exposes public member <c>Context.Builder</c>.
    /// </summary>
    public CppModelBuilder Builder => Context.Builder;

    /// <summary>
    /// Gets or sets <c>Container</c>.
    /// </summary>
    public CppContainerContext Container { get; internal set; } = null!;

    /// <summary>
    /// Exposes public member <c>Context.CurrentRootContainer</c>.
    /// </summary>
    public CppContainerContext CurrentRootContainer => Context.CurrentRootContainer;

    /// <summary>
    /// Exposes public member <c>Context.TypedefResolver</c>.
    /// </summary>
    public TypedefResolver TypedefResolver => Context.TypedefResolver;

    /// <summary>
    /// Exposes public member <c>Context.RootCompilation</c>.
    /// </summary>
    public CppCompilation RootCompilation => Context.RootCompilation;

    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public abstract IEnumerable<CXCursorKind> Kinds { get; }

    /// <summary>
    /// Gets <c>VisitResult</c>.
    /// </summary>
    public virtual CXChildVisitResult VisitResult { get; } = CXChildVisitResult.CXChildVisit_Continue;

    /// <summary>
    /// Executes public operation <c>Visit</c>.
    /// </summary>
    public unsafe TResult Visit(CppModelContext context, CXCursor cursor, CXCursor parent)
    {
        Context = context;
        return VisitCore(cursor, parent);
    }

    protected abstract unsafe TResult VisitCore(CXCursor cursor, CXCursor parent);
}
