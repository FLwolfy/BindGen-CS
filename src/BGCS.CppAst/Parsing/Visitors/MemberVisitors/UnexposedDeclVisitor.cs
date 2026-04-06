using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>UnexposedDeclVisitor</c>.
/// </summary>
public class UnexposedDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_UnexposedDecl];

    protected override unsafe CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        cursor.VisitChildren(Builder.VisitMember, default);
        return null;
    }
}
