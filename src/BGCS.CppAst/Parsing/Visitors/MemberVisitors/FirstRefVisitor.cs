namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>FirstRefVisitor</c>.
/// </summary>
internal class FirstRefVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_FirstRef];

    protected override unsafe CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        return null;
    }
}
