using System;
using System.IO;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>InclusionDirectiveVisitor</c>.
/// </summary>
public unsafe class InclusionDirectiveVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_InclusionDirective
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var file = cursor.IncludedFile;
        CppInclusionDirective inclusionDirective = new(cursor, Path.GetFullPath(file.Name.ToString()));
        var rootContainer = (CppGlobalDeclarationContainer)CurrentRootContainer.DeclarationContainer;
        rootContainer.InclusionDirectives.Add(inclusionDirective);
        return inclusionDirective;
    }
}
