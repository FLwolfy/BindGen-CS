using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

public unsafe class CXXAccessSpecifierVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_CXXAccessSpecifier
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var containerContext = Context.GetOrCreateDeclContainer(parent);
        containerContext.CurrentVisibility = cursor.GetVisibility();
        return null;
    }
}
