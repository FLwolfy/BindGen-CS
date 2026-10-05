namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>TypeAliasDeclVisitor</c>.
/// </summary>
internal unsafe class TypeAliasDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_TypeAliasDecl, CXCursorKind.CXCursor_TypeAliasTemplateDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var fulltypeDefName = this.context.GetCursorKey(cursor);
        if (this.typedefResolver.TryResolve(fulltypeDefName, out var type))
        {
            return type;
        }

        var contextContainer = this.context.GetOrCreateDeclContainer(cursor.SemanticParent);
        var kind = cursor.Kind;
        CXCursor usedCursor = cursor;
        if (kind == CXCursorKind.CXCursor_TypeAliasTemplateDecl)
        {
            usedCursor = cursor.TemplatedDecl;
        }

        var underlyingTypeDefType = this.builder.GetCppType(usedCursor.TypedefDeclUnderlyingType.Declaration, usedCursor.TypedefDeclUnderlyingType, usedCursor);
        var typedefName = CXUtil.GetCursorSpelling(usedCursor);
        if (this.builder.autoSquashTypedef && underlyingTypeDefType is ICppMember cppMember && (string.IsNullOrEmpty(cppMember.name) || typedefName == cppMember.name))
        {
            cppMember.name = typedefName;
            type = (CppType)cppMember;
        }
        else
        {
            CppTypedef typedef = new(cursor, typedefName, underlyingTypeDefType)
            {
                visibility = contextContainer.currentVisibility
            };
            contextContainer.declarationContainer.typedefs.Add(typedef);
            type = typedef;
        }

        this.builder.ParseTypedefAttribute(cursor, type, underlyingTypeDefType);
        this.typedefResolver.RegisterTypedef(fulltypeDefName, type);
        return type;
    }
}
