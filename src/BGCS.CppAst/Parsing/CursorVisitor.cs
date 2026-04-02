using System;
using System.Collections.Generic;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Parsing;
public abstract class CursorVisitor<TResult>
{
    public CppModelContext Context { get; internal set; } = null!;

    public CppModelBuilder Builder => Context.Builder;

    public CppContainerContext Container { get; internal set; } = null!;

    public CppContainerContext CurrentRootContainer => Context.CurrentRootContainer;

    public TypedefResolver TypedefResolver => Context.TypedefResolver;

    public CppCompilation RootCompilation => Context.RootCompilation;

    public abstract IEnumerable<CXCursorKind> Kinds { get; }

    public virtual CXChildVisitResult VisitResult { get; } = CXChildVisitResult.CXChildVisit_Continue;

    public unsafe TResult Visit(CppModelContext context, CXCursor cursor, CXCursor parent)
    {
        Context = context;
        return VisitCore(cursor, parent);
    }

    protected abstract unsafe TResult VisitCore(CXCursor cursor, CXCursor parent);
}
