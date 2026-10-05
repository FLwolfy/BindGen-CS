namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>TypeRefVisitor</c>.
/// </summary>
internal unsafe class TypeRefVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_TypeRef];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        if (this.context.currentClassBeingVisited != null && this.context.currentClassBeingVisited.baseTypes.Count == 1)
        {
            var baseType = this.context.currentClassBeingVisited.baseTypes[0].type;
            CppGenericType genericType = baseType as CppGenericType ?? new CppGenericType(cursor, baseType);
            var type = this.builder.GetCppType(cursor.Referenced, cursor.Type, cursor);
            genericType.genericArguments.Add(type);
        }

        return null;
    }
}
