using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>ObjCClassProtocolRefVisitor</c>.
/// </summary>
public unsafe class ObjCClassProtocolRefVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_ObjCClassRef,
            CXCursorKind.CXCursor_ObjCProtocolRef
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var objCContainer = Context.GetOrCreateDeclContainer(parent).Container;
        if (objCContainer is CppClass cppClass && cppClass.ClassKind != CppClassKind.ObjCInterfaceCategory)
        {
            var referencedType = (CppClass)Context.GetOrCreateDeclContainer(cursor.Referenced).Container;
            if (cursor.Kind == CXCursorKind.CXCursor_ObjCClassRef)
            {
                var cppBaseType = new CppBaseType(cursor, referencedType);
                cppClass.BaseTypes.Add(cppBaseType);
            }
            else
            {
                cppClass.ObjCImplementedProtocols.Add(referencedType);
            }
        }
        return null;
    }
}
