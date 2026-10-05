namespace BGCS.CppAst.Parsing.Visitors;

using System.Collections.Generic;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>EnumDeclVisitor</c>.
/// </summary>
internal class EnumDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_EnumDecl];

    protected override unsafe CppContainerContext VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var parentContainer = this.context.GetOrCreateDeclContainer(cursor.SemanticParent).declarationContainer;
        CppEnum cppEnum = new(cursor, CXUtil.GetCursorSpelling(cursor))
        {
            isAnonymous = cursor.IsAnonymous,
            visibility = cursor.GetVisibility()
        };
        parentContainer.enums.Add(cppEnum);
        return new(cppEnum);
    }
}
