namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>LinkageSpecVisitor</c>.
/// </summary>
internal unsafe class LinkageSpecVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_LinkageSpec];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        cursor.VisitChildren(this.builder.VisitMember, default);
        return null;
    }
}
