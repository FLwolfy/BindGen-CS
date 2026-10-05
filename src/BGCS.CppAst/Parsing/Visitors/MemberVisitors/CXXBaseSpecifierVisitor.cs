namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CXXBaseSpecifierVisitor</c>.
/// </summary>
internal unsafe class CXXBaseSpecifierVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_CXXBaseSpecifier];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var cppClass = this.context.GetOrCreateDeclContainer<CppClass>(parent, out _);
        var baseType = this.builder.GetCppType(cursor.Type.Declaration, cursor.Type, cursor);
        var cppBaseType = new CppBaseType(cursor, baseType)
        {
            visibility = cursor.GetVisibility(),
            isVirtual = cursor.IsVirtualBase
        };
        cppClass.baseTypes.Add(cppBaseType);
        return null;
    }
}
