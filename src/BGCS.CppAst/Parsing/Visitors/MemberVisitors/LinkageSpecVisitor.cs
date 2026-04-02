using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

public unsafe class LinkageSpecVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_LinkageSpec
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        cursor.VisitChildren(Builder.VisitMember, default);
        return null;
    }
}
