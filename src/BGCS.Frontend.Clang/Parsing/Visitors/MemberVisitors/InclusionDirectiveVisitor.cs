using System.IO;

namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Collections;
using BGCS.Frontend.Clang.Model;
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
