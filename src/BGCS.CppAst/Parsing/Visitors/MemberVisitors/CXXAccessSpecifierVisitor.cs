using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>CXXAccessSpecifierVisitor</c>.
/// </summary>
public unsafe class CXXAccessSpecifierVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
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
