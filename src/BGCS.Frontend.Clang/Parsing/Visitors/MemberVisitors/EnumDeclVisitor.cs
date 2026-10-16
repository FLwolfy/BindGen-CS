namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>EnumDeclMemberVisitor</c>.
/// </summary>
internal unsafe class EnumDeclMemberVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_EnumDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var cppEnum = this.context.GetOrCreateDeclContainer<CppEnum>(cursor, out var context);
        if (cursor.IsDefinition && !context.isChildrenVisited)
        {
            var integralType = cursor.EnumDecl_IntegerType;
            cppEnum.integerType = this.builder.GetCppType(integralType.Declaration, integralType, cursor);
            cppEnum.isScoped = cursor.EnumDecl_IsScoped;
            this.builder.ParseAttributes(cursor, cppEnum);
            context.isChildrenVisited = true;
            cursor.VisitChildren(this.builder.VisitMember, default);
        }

        return cppEnum;
    }
}
