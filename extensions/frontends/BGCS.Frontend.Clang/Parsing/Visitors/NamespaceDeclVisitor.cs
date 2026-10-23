namespace BGCS.Frontend.Clang.Parsing.Visitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Parsing;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>NamespaceDeclVisitor</c>.
/// </summary>
internal class NamespaceDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_Namespace];

    protected override unsafe CppContainerContext VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var parentContainer = this.context.GetOrCreateDeclContainer(cursor.SemanticParent).globalDeclarationContainer;
        CppNamespace ns = new(cursor, CXUtil.GetCursorSpelling(cursor))
        {
            isInlineNamespace = cursor.IsInlineNamespace
        };
        parentContainer.namespaces.Add(ns);
        return new(ns);
    }
}
