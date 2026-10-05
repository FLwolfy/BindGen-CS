namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>ObjCClassProtocolRefVisitor</c>.
/// </summary>
internal unsafe class ObjCClassProtocolRefVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_ObjCClassRef, CXCursorKind.CXCursor_ObjCProtocolRef];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var objCContainer = this.context.GetOrCreateDeclContainer(parent).container;
        if (objCContainer is CppClass cppClass && cppClass.classKind != CppClassKind.ObjCInterfaceCategory)
        {
            var referencedType = (CppClass)this.context.GetOrCreateDeclContainer(cursor.Referenced).container;
            if (cursor.Kind == CXCursorKind.CXCursor_ObjCClassRef)
            {
                var cppBaseType = new CppBaseType(cursor, referencedType);
                cppClass.baseTypes.Add(cppBaseType);
            }
            else
            {
                cppClass.objCImplementedProtocols.Add(referencedType);
            }
        }

        return null;
    }
}
