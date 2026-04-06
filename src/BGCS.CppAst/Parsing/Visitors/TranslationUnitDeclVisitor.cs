using System;
namespace BGCS.CppAst.Parsing.Visitors;
using ClangSharp.Interop;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>TranslationUnitDeclVisitor</c>.
/// </summary>
public class TranslationUnitDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_TranslationUnit, CXCursorKind.CXCursor_UnexposedDecl, CXCursorKind.CXCursor_FirstInvalid];

    protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
    {
        return Builder.CurrentRootContainer;
    }
}
