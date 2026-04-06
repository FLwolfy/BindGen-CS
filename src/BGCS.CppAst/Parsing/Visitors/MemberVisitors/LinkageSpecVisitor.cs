using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>LinkageSpecVisitor</c>.
/// </summary>
public unsafe class LinkageSpecVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_LinkageSpec
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        cursor.VisitChildren(Builder.VisitMember, default);
        return null;
    }
}
