namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using System.Diagnostics;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>ClassStructUnionObjCVisitor</c>.
/// </summary>
internal unsafe class ClassStructUnionObjCVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_ClassTemplate, CXCursorKind.CXCursor_ClassDecl, CXCursorKind.CXCursor_StructDecl, CXCursorKind.CXCursor_UnionDecl, CXCursorKind.CXCursor_ObjCInterfaceDecl, CXCursorKind.CXCursor_ObjCProtocolDecl, CXCursorKind.CXCursor_ObjCCategoryDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        bool isAnonymous = cursor.IsAnonymous;
        var cppClass = this.builder.VisitClassDecl(cursor);
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        // Empty struct/class/union declaration are considered as fields
        if (isAnonymous)
        {
            cppClass.name = string.Empty;
            Debug.Assert(string.IsNullOrEmpty(cppClass.name));
            // We try to recover the offset from the previous field
            // Might not be always correct (with alignment rules),
            // but not sure how to recover the offset without recalculating the entire offsets
            var offset = 0;
            if (containerContext.container is CppClass cppClassContainer && cppClassContainer.fields.Count > 0)
            {
                var lastField = cppClassContainer.fields[^1];
                offset = (int)lastField.offset + lastField.type.sizeOf;
            }

            // Create an anonymous field for the type
            var cppField = new CppField(cursor, cppClass, string.Empty)
            {
                visibility = containerContext.currentVisibility,
                storageQualifier = cursor.GetStorageQualifier(),
                isAnonymous = true,
                bitOffset = offset * 8,
            };
            this.builder.ParseAttributes(cursor, cppField, true);
            containerContext.declarationContainer.fields.Add(cppField);
            return cppField;
        }
        else
        {
            cppClass.visibility = containerContext.currentVisibility;
            return cppClass;
        }
    }
}
