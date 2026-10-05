namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>UnexposedDeclVisitor</c>.
/// </summary>
internal class UnexposedDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_UnexposedDecl];

    protected override unsafe CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        cursor.VisitChildren(this.builder.VisitMember, default);
        return null;
    }
}
