namespace BGCS.Frontend.Clang.Parsing.Visitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Parsing;
using BGCS.Frontend.Clang.Utilities;
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
        CppEnum cppEnum = new(cursor, CXUtil.GetDeclarationName(cursor))
        {
            isAnonymous = cursor.IsAnonymous,
            visibility = cursor.GetVisibility()
        };
        parentContainer.enums.Add(cppEnum);
        return new(cppEnum);
    }
}
