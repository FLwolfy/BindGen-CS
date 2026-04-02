using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using System.Collections.Generic;

public unsafe class CXXBaseSpecifierVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_CXXBaseSpecifier
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var cppClass = Context.GetOrCreateDeclContainer<CppClass>(parent, out _);
        var baseType = Builder.GetCppType(cursor.Type.Declaration, cursor.Type, cursor);
        var cppBaseType = new CppBaseType(cursor, baseType)
        {
            Visibility = cursor.GetVisibility(),
            IsVirtual = cursor.IsVirtualBase
        };
        cppClass.BaseTypes.Add(cppBaseType);
        return null;
    }
}
