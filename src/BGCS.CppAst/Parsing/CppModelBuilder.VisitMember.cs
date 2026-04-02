using System;
namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;

public unsafe partial class CppModelBuilder
{
    public CXChildVisitResult VisitMember(CXCursor cursor, CXCursor parent, void* data = null)
    {
        CppElement? element = null;

        // Only set the root container when we know the location
        // Otherwise assume that it hasn't changed
        // We expect it to be always set
        if (cursor.Location != CXSourceLocation.Null)
        {
            if (cursor.Location.IsInSystemHeader)
            {
                if (!ParseSystemIncludes) return CXChildVisitResult.CXChildVisit_Continue;

                context.CurrentRootContainer = context.SystemRootContainerContext;
            }
            else
            {
                context.CurrentRootContainer = context.UserRootContainerContext;
            }
        }

        if (context.CurrentRootContainer is null)
        {
            RootCompilation.Diagnostics.Error($"Unexpected error with cursor location. Cannot determine Root Compilation context.");
            return CXChildVisitResult.CXChildVisit_Continue;
        }

        var visitor = MemberVisitorRegistry.GetVisitor(cursor.Kind);
        if (visitor != null)
        {
            element = visitor.Visit(context, cursor, parent);
        }
        else
        {
            if (!cursor.IsAttribute)
            {
                WarningUnhandled(cursor, parent);
            }
        }

        if (element == null)
        {
            return CXChildVisitResult.CXChildVisit_Continue;
        }

        if (element.SourceFile is null || cursor.IsCursorDefinition(element))
        {
            element.AssignSourceSpan(cursor);
        }

        if (element is ICppDeclaration cppDeclaration)
        {
            cppDeclaration.Comment = cursor.GetComment();

            if (cppDeclaration is ICppAttributeContainer attrContainer && ParseCommentAttributeEnabled)
            {
                cppDeclaration.Comment?.TryToParseAttributes(attrContainer);
            }
        }

        element.ConvertToMetaAttributes();

        return visitor!.VisitResult;
    }
}
