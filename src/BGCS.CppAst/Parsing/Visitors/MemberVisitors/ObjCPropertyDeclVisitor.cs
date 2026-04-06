using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Utilities;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>ObjCPropertyDeclVisitor</c>.
/// </summary>
public unsafe class ObjCPropertyDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_ObjCPropertyDecl
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var containerContext = Context.GetOrCreateDeclContainer(parent);
        var propertyName = CXUtil.GetCursorSpelling(cursor);
        var type = Builder.GetCppType(cursor.Type.Declaration, cursor.Type, cursor);

        CppProperty cppProperty = new(cursor, type, propertyName);
        cppProperty.GetterName = cursor.ObjCPropertyGetterName.ToString();
        cppProperty.SetterName = cursor.ObjCPropertySetterName.ToString();
        Builder.ParseAttributes(cursor, cppProperty, true);
        containerContext.DeclarationContainer.Properties.Add(cppProperty);
        return cppProperty;
    }
}
