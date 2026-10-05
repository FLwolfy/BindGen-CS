namespace BGCS.CppAst.Parsing.Visitors;

using System.Collections.Generic;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
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
