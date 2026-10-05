namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using ClangSharp.Interop;

internal class TemplateTypeParameterVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_TemplateTypeParameter];

    protected override unsafe CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        return null;
    }
}
