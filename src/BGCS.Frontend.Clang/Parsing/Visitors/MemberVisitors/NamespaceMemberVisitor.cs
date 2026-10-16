using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

/// <summary>
/// Defines the public class <c>NamespaceMemberVisitor</c>.
/// </summary>
internal class NamespaceMemberVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_Namespace];

    protected override unsafe CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var ns = this.context.GetOrCreateDeclContainer<CppNamespace>(cursor, out var context);
        this.builder.ParseAttributes(cursor, ns, false);
        cursor.VisitChildren(this.builder.VisitMember, default);
        return ns;
    }
}
