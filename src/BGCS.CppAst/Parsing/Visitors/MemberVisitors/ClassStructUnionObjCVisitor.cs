using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using System.Collections.Generic;
using System.Diagnostics;

public unsafe class ClassStructUnionObjCVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_ClassTemplate,
            CXCursorKind.CXCursor_ClassDecl,
            CXCursorKind.CXCursor_StructDecl,
            CXCursorKind.CXCursor_UnionDecl,
            CXCursorKind.CXCursor_ObjCInterfaceDecl,
            CXCursorKind.CXCursor_ObjCProtocolDecl,
            CXCursorKind.CXCursor_ObjCCategoryDecl
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        bool isAnonymous = cursor.IsAnonymous;
        var cppClass = Builder.VisitClassDecl(cursor);
        var containerContext = Context.GetOrCreateDeclContainer(parent);
        // Empty struct/class/union declaration are considered as fields
        if (isAnonymous)
        {
            cppClass.Name = string.Empty;
            Debug.Assert(string.IsNullOrEmpty(cppClass.Name));

            // We try to recover the offset from the previous field
            // Might not be always correct (with alignment rules),
            // but not sure how to recover the offset without recalculating the entire offsets
            var offset = 0;
            if (containerContext.Container is CppClass cppClassContainer && cppClassContainer.Fields.Count > 0)
            {
                var lastField = cppClassContainer.Fields[^1];
                offset = (int)lastField.Offset + lastField.Type.SizeOf;
            }

            // Create an anonymous field for the type
            var cppField = new CppField(cursor, cppClass, string.Empty)
            {
                Visibility = containerContext.CurrentVisibility,
                StorageQualifier = cursor.GetStorageQualifier(),
                IsAnonymous = true,
                BitOffset = offset * 8,
            };
            Builder.ParseAttributes(cursor, cppField, true);
            containerContext.DeclarationContainer.Fields.Add(cppField);
            return cppField;
        }
        else
        {
            cppClass.Visibility = containerContext.CurrentVisibility;
            return cppClass;
        }
    }
}
