using System.Collections.Generic;
using System.Linq;

using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Utilities;

namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
/// <summary>
/// Defines the public class <c>FieldVariableVisitor</c>.
/// </summary>
public unsafe class FieldVariableVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } = [CXCursorKind.CXCursor_FieldDecl, CXCursorKind.CXCursor_VarDecl];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        var containerContext = Context.GetOrCreateDeclContainer(parent);
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

        var type = Builder.GetCppType(sourceDeclaration, sourceType, cursor);

        var previousField = containerContext.DeclarationContainer.Fields.LastOrDefault();
        CppField cppField;

        // This happen in the type is anonymous, we create implicitly a field for it, but if type is the same
        // we should reuse the anonymous field we created just before
        if (previousField != null && previousField.IsAnonymous && type.IsAnonymousTypeUsed(previousField.Type))
        {
            cppField = previousField;
            cppField.Name = fieldName;
            cppField.Type = type;
            cppField.BitOffset = cursor.OffsetOfField;
        }
        else
        {
            cppField = new(cursor, type, fieldName)
            {
                Visibility = cursor.GetVisibility(),
                StorageQualifier = cursor.GetStorageQualifier(),
                IsBitField = cursor.IsBitField,
                BitFieldWidth = cursor.FieldDeclBitWidth,
                BitOffset = cursor.OffsetOfField,
            };
            containerContext.DeclarationContainer.Fields.Add(cppField);
            Builder.ParseAttributes(cursor, cppField, true);

            if (cursor.Kind == CXCursorKind.CXCursor_VarDecl)
            {
                Builder.VisitInitValue(cursor, out var fieldExpr, out var fieldValue);
                cppField.InitValue = fieldValue;
                cppField.InitExpression = fieldExpr;
            }
        }

        return cppField;
    }
}
