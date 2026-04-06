using System;
namespace BGCS.CppAst.Parsing.Visitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>EnumDeclVisitor</c>.
/// </summary>
public class EnumDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_EnumDecl];

    protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
    {
        var parentContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent).DeclarationContainer;
        CppEnum cppEnum = new(cursor, CXUtil.GetCursorSpelling(cursor))
        {
            IsAnonymous = cursor.IsAnonymous,
            Visibility = cursor.GetVisibility()
        };

        parentContainer.Enums.Add(cppEnum);
        return new(cppEnum);
    }
}
