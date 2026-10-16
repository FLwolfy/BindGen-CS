using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

/// <summary>
/// Defines the public class <c>EnumConstantVisitor</c>.
/// </summary>
internal class EnumConstantVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_EnumConstantDecl];

    protected override unsafe CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        var cppEnum = (CppEnum)containerContext.container;
        var enumItem = new CppEnumItem(cursor, CXUtil.GetCursorSpelling(cursor), cursor.EnumConstantDeclValue);
        this.builder.ParseAttributes(cursor, enumItem, true);
        this.builder.VisitInitValue(cursor, out var enumItemExpression, out var enumValue);
        enumItem.valueExpression = enumItemExpression;
        cppEnum.items.Add(enumItem);
        return enumItem;
    }
}
