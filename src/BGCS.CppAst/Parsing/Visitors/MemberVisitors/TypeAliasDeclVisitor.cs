using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using System.Collections.Generic;

/// <summary>
/// Defines the public class <c>TypeAliasDeclVisitor</c>.
/// </summary>
public unsafe class TypeAliasDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_TypeAliasDecl,
            CXCursorKind.CXCursor_TypeAliasTemplateDecl
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var fulltypeDefName = Context.GetCursorKey(cursor);
        if (TypedefResolver.TryResolve(fulltypeDefName, out var type))
        {
            return type;
        }

        var contextContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent);

        var kind = cursor.Kind;

        CXCursor usedCursor = cursor;
        if (kind == CXCursorKind.CXCursor_TypeAliasTemplateDecl)
        {
            usedCursor = cursor.TemplatedDecl;
        }

        var underlyingTypeDefType = Builder.GetCppType(usedCursor.TypedefDeclUnderlyingType.Declaration, usedCursor.TypedefDeclUnderlyingType, usedCursor);
        var typedefName = CXUtil.GetCursorSpelling(usedCursor);

        if (Builder.AutoSquashTypedef && underlyingTypeDefType is ICppMember cppMember && (string.IsNullOrEmpty(cppMember.Name) || typedefName == cppMember.Name))
        {
            cppMember.Name = typedefName;
            type = (CppType)cppMember;
        }
        else
        {
            CppTypedef typedef = new(cursor, typedefName, underlyingTypeDefType) { Visibility = contextContainer.CurrentVisibility };
            contextContainer.DeclarationContainer.Typedefs.Add(typedef);
            type = typedef;
        }

        Builder.ParseTypedefAttribute(cursor, type, underlyingTypeDefType);

        TypedefResolver.RegisterTypedef(fulltypeDefName, type);

        return type;
    }
}
