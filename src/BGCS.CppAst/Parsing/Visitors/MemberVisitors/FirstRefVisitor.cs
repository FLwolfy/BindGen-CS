using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

public class FirstRefVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_FirstRef];

    protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        return null;
    }
}
