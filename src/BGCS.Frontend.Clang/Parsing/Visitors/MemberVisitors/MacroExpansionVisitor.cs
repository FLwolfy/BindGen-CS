namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using ClangSharp.Interop;

internal class MacroExpansionVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_MacroExpansion];

    protected override unsafe CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        return null;
    }
}
