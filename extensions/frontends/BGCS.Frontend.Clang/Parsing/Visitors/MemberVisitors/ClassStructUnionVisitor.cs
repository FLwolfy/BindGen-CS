namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>ClassStructUnionVisitor</c>.
/// </summary>
internal unsafe class ClassStructUnionVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_ClassTemplate, CXCursorKind.CXCursor_ClassDecl, CXCursorKind.CXCursor_StructDecl, CXCursorKind.CXCursor_UnionDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        // The injected self name is represented by its existing semantic owner.
        if (cursor.IsInjectedClassName) return null;
        bool isAnonymous = cursor.IsAnonymousStructOrUnion;
        var cppClass = this.builder.VisitClassDecl(cursor);
        var containerContext = this.context.GetOrCreateDeclContainer(parent);
        // Only injected anonymous storage is an implicit field. An unnamed
        // type used by a named field keeps its independent declaration.
        if (isAnonymous)
        {
            cppClass.name = string.Empty;
            long bitOffset = -1;
            for (uint index = 0; index < parent.NumDecls; index++)
            {
                CXCursor field = parent.GetDecl(index);
                if (field.Kind == CXCursorKind.CXCursor_FieldDecl && field.Type.CanonicalType.Declaration.CanonicalCursor == cursor.CanonicalCursor)
                {
                    bitOffset = field.OffsetOfField;
                    break;
                }
            }
            // A dependent template legitimately has no layout yet. Preserve
            // that unknown value; selected storage verification rejects it
            // unless a concrete instantiation supplies a real field offset.

            // Create an anonymous field for the type
            CppVisibility declaredAccess = cursor.GetVisibility();
            var cppField = new CppField(cursor, cppClass, string.Empty)
            {
                visibility = declaredAccess == CppVisibility.Default ? containerContext.currentVisibility : declaredAccess,
                storageQualifier = cursor.GetStorageQualifier(),
                isAnonymous = true,
                bitOffset = bitOffset,
            };
            this.builder.ParseAttributes(cursor, cppField, true);
            containerContext.declarationContainer.fields.Add(cppField);
            return cppField;
        }
        else
        {
            // Instantiated cursors can be enumerated without their lexical access
            // markers. Keep the access already established by the native cursor.
            if (cppClass.visibility == CppVisibility.Default)
                cppClass.visibility = containerContext.currentVisibility;
            return cppClass;
        }
    }
}
