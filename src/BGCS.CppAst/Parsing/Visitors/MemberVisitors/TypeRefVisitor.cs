using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Types;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>TypeRefVisitor</c>.
/// </summary>
public unsafe class TypeRefVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_TypeRef
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        if (Context.CurrentClassBeingVisited != null && Context.CurrentClassBeingVisited.BaseTypes.Count == 1)
        {
            var baseType = Context.CurrentClassBeingVisited.BaseTypes[0].Type;
            CppGenericType genericType = baseType as CppGenericType ?? new CppGenericType(cursor, baseType);
            var type = Builder.GetCppType(cursor.Referenced, cursor.Type, cursor);
            genericType.GenericArguments.Add(type);
        }
        return null;
    }
}
