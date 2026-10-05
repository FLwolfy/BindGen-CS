namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CXXAccessSpecifierVisitor</c>.
/// </summary>
internal unsafe class CXXAccessSpecifierVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_CXXAccessSpecifier];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        containerContext.currentVisibility = cursor.GetVisibility();
        return null;
    }
}
