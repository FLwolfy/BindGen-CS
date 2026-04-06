using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>EnumDeclMemberVisitor</c>.
/// </summary>
public unsafe class EnumDeclMemberVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_EnumDecl
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var cppEnum = Context.GetOrCreateDeclContainer<CppEnum>(cursor, out var context);
        if (cursor.IsDefinition && !context.IsChildrenVisited)
        {
            var integralType = cursor.EnumDecl_IntegerType;
            cppEnum.IntegerType = Builder.GetCppType(integralType.Declaration, integralType, cursor);
            cppEnum.IsScoped = cursor.EnumDecl_IsScoped;
            Builder.ParseAttributes(cursor, cppEnum);
            context.IsChildrenVisited = true;
            cursor.VisitChildren(Builder.VisitMember, default);
        }
        return cppEnum;
    }
}
