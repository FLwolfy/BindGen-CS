using System;
namespace BGCS.CppAst.Parsing.Visitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using System.Collections.Generic;

public class NamespaceDeclVisitor : DeclContainerVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_Namespace];

    protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
    {
        var parentContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent).GlobalDeclarationContainer;
        CppNamespace ns = new(cursor, CXUtil.GetCursorSpelling(cursor))
        {
            IsInlineNamespace = cursor.IsInlineNamespace
        };

        parentContainer.Namespaces.Add(ns);
        return new(ns);
    }
}
