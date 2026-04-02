using System;
using System.Collections.Generic;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;

namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
public class NamespaceMemberVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_Namespace];

    protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var ns = Context.GetOrCreateDeclContainer<CppNamespace>(cursor, out var context);
        Builder.ParseAttributes(cursor, ns, false);
        cursor.VisitChildren(Builder.VisitMember, default);
        return ns;
    }
}
