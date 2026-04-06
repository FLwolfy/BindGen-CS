using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>FlagEnumVisitor</c>.
/// </summary>
public unsafe class FlagEnumVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_FlagEnum
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var containerContext = Context.GetOrCreateDeclContainer(parent);
        var cppEnum = (CppEnum)containerContext.Container;
        cppEnum.Attributes.Add(new CppAttribute(cursor, "flag_enum", AttributeKind.ObjectiveCAttribute));
        return null;
    }
}
