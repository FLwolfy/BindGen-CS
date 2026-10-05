using System.IO;

namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>InclusionDirectiveVisitor</c>.
/// </summary>
internal unsafe class InclusionDirectiveVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_InclusionDirective];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var file = cursor.IncludedFile;
        CppInclusionDirective inclusionDirective = new(cursor, Path.GetFullPath(file.Name.ToString()));
        var rootContainer = (CppGlobalDeclarationContainer)this.currentRootContainer.declarationContainer;
        rootContainer.inclusionDirectives.Add(inclusionDirective);
        return inclusionDirective;
    }
}
