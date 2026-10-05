using System.Linq;

namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>TypedefDeclVisitor</c>.
/// </summary>
internal unsafe class TypedefDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_TypedefDecl];

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
        this.context.currentTypedefKey = fulltypeDefName;
        var underlyingTypeDefType = this.builder.GetCppType(cursor.TypedefDeclUnderlyingType.Declaration, cursor.TypedefDeclUnderlyingType, cursor);
        this.context.currentTypedefKey = default;
        var typedefName = CXUtil.GetCursorSpelling(cursor);
        ICppDeclarationContainer? container = null;
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
            container = contextContainer.declarationContainer;
            type = typedef;
        }

        this.builder.ParseTypedefAttribute(cursor, type, underlyingTypeDefType);
        // The type could have been added separately as part of the GetCppType above
        this.typedefResolver.RegisterTypedef(fulltypeDefName, type);
        var map = this.context.mapTemplateParameterTypeToTypedefKeys;
        // Try to remap typedef using a parameter type declared in an ObjC interface
        if (map.Count > 0)
        {
            foreach (var pair in map.ToList())
            {
                if (pair.Value.Contains(fulltypeDefName))
                {
                    container = (ICppDeclarationContainer?)pair.Key.parent;
                    map.Remove(pair.Key);
                    break;
                }
            }
        }

        container?.typedefs.Add((CppTypedef)type);
        // Update Span
        if (type is CppElement element)
        {
            element.AssignSourceSpan(cursor);
            if (element is CppTypedef typedef && typedef.elementType is CppClass && string.IsNullOrWhiteSpace(typedef.elementType.sourceFile))
            {
                typedef.elementType.span = element.span;
            }
        }

        return type;
    }
}
