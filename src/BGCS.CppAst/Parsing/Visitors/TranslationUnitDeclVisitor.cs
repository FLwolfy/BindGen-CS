using System;
namespace BGCS.CppAst.Parsing.Visitors;
using ClangSharp.Interop;
using System.Collections.Generic;

public class TranslationUnitDeclVisitor : DeclContainerVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_TranslationUnit, CXCursorKind.CXCursor_UnexposedDecl, CXCursorKind.CXCursor_FirstInvalid];

    protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
    {
        return Builder.CurrentRootContainer;
    }
}
