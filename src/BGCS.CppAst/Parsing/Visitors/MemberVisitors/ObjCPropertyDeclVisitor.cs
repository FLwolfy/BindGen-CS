namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>ObjCPropertyDeclVisitor</c>.
/// </summary>
internal unsafe class ObjCPropertyDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_ObjCPropertyDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        var propertyName = CXUtil.GetCursorSpelling(cursor);
        var type = this.builder.GetCppType(cursor.Type.Declaration, cursor.Type, cursor);
        CppProperty cppProperty = new(cursor, type, propertyName);
        cppProperty.getterName = cursor.ObjCPropertyGetterName.ToString();
        cppProperty.setterName = cursor.ObjCPropertySetterName.ToString();
        this.builder.ParseAttributes(cursor, cppProperty, true);
        containerContext.declarationContainer.properties.Add(cppProperty);
        return cppProperty;
    }
}
