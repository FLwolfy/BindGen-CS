using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

public class UnexposedDeclVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_UnexposedDecl];

    protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        cursor.VisitChildren(Builder.VisitMember, default);
        return null;
    }
}
