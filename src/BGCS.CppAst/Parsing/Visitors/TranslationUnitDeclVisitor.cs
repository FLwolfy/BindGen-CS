namespace BGCS.CppAst.Parsing.Visitors;

using System.Collections.Generic;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>TranslationUnitDeclVisitor</c>.
/// </summary>
internal class TranslationUnitDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_TranslationUnit, CXCursorKind.CXCursor_UnexposedDecl, CXCursorKind.CXCursor_FirstInvalid];

    protected override unsafe CppContainerContext VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        return this.builder.currentRootContainer;
    }
}
