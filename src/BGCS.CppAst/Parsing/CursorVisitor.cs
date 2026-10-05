using System.Collections.Generic;
using BGCS.CppAst.Model.Metadata;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Defines the public class <c>CursorVisitor</c>.
/// </summary>
internal abstract class CursorVisitor<TResult>
{
    /// <summary>
    /// Gets or sets <c>Context</c>.
    /// </summary>
    public CppModelContext context { get; internal set; } = null!;
    /// <summary>
    /// Exposes public member <c>Context.Builder</c>.
    /// </summary>
    public CppModelBuilder builder => this.context.builder;
    /// <summary>
    /// Gets or sets <c>Container</c>.
    /// </summary>
    public CppContainerContext container { get; internal set; } = null!;
    /// <summary>
    /// Exposes public member <c>Context.CurrentRootContainer</c>.
    /// </summary>
    public CppContainerContext currentRootContainer => this.context.currentRootContainer;
    /// <summary>
    /// Exposes public member <c>Context.TypedefResolver</c>.
    /// </summary>
    public TypedefResolver typedefResolver => this.context.typedefResolver;
    /// <summary>
    /// Exposes public member <c>Context.RootCompilation</c>.
    /// </summary>
    public CppCompilation rootCompilation => this.context.rootCompilation;
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public abstract IEnumerable<CXCursorKind> kinds { get; }
    /// <summary>
    /// Gets <c>VisitResult</c>.
    /// </summary>
    public virtual CXChildVisitResult visitResult { get; } = CXChildVisitResult.CXChildVisit_Continue;

    /// <summary>
    /// Executes public operation <c>Visit</c>.
    /// </summary>
    public unsafe TResult Visit(
        CppModelContext context,
        CXCursor cursor,
        CXCursor parent
    ) {
        this.context = context;
        return VisitCore(cursor, parent);
    }

    protected abstract unsafe TResult VisitCore(
        CXCursor cursor,
        CXCursor parent
    );
}
