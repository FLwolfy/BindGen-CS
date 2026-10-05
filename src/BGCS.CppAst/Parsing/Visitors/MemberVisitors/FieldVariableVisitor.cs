using System.Collections.Generic;
using System.Linq;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

/// <summary>
/// Defines the public class <c>FieldVariableVisitor</c>.
/// </summary>
internal unsafe class FieldVariableVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_FieldDecl, CXCursorKind.CXCursor_VarDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        var fieldName = CXUtil.GetCursorSpelling(cursor);
        var sourceType = cursor.Type;
        var sourceDeclaration = sourceType.Declaration;
        // Keep typedef identity for field/variable types instead of always using
        // the potentially decayed cursor type (e.g. function pointer aliases).
        if (sourceDeclaration.Kind == CXCursorKind.CXCursor_TypedefDecl)
        {
            var typedefType = sourceDeclaration.Type;
            if (typedefType.kind == CXTypeKind.CXType_Typedef)
            {
                sourceType = typedefType;
                sourceDeclaration = sourceType.Declaration;
            }
        }

        var type = this.builder.GetCppType(sourceDeclaration, sourceType, cursor);
        var previousField = containerContext.declarationContainer.fields.LastOrDefault();
        CppField cppField;
        // This happen in the type is anonymous, we create implicitly a field for it, but if type is the same
        // we should reuse the anonymous field we created just before
        if (previousField != null && previousField.isAnonymous && type.IsAnonymousTypeUsed(previousField.type))
        {
            cppField = previousField;
            cppField.name = fieldName;
            cppField.type = type;
            cppField.bitOffset = cursor.OffsetOfField;
        }
        else
        {
            cppField = new(cursor, type, fieldName)
            {
                visibility = cursor.GetVisibility(),
                storageQualifier = cursor.GetStorageQualifier(),
                isBitField = cursor.IsBitField,
                bitFieldWidth = cursor.FieldDeclBitWidth,
                bitOffset = cursor.OffsetOfField,
            };
            containerContext.declarationContainer.fields.Add(cppField);
            this.builder.ParseAttributes(cursor, cppField, true);
            if (cursor.Kind == CXCursorKind.CXCursor_VarDecl)
            {
                this.builder.VisitInitValue(cursor, out var fieldExpr, out var fieldValue);
                cppField.initValue = fieldValue;
                cppField.initExpression = fieldExpr;
            }
        }

        return cppField;
    }
}
