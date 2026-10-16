namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Attributes;
using BGCS.Frontend.Clang.Model.Declarations;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>FlagEnumVisitor</c>.
/// </summary>
internal unsafe class FlagEnumVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_FlagEnum];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        var cppEnum = (CppEnum)containerContext.container;
        cppEnum.attributes.Add(new CppAttribute(cursor, "flag_enum", AttributeKind.ObjectiveCAttribute));
        return null;
    }
}
