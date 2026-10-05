namespace BGCS.CppAst.Parsing;

using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CppModelBuilder</c>.
/// </summary>
internal unsafe partial class CppModelBuilder
{
    /// <summary>
    /// Executes public operation <c>VisitMember</c>.
    /// </summary>
    public CXChildVisitResult VisitMember(
        CXCursor cursor,
        CXCursor parent,
        void* data = null
    ) {
        CppElement? element = null;
        // Only set the root container when we know the location
        // Otherwise assume that it hasn't changed
        // We expect it to be always set
        if (cursor.Location != CXSourceLocation.Null)
        {
            if (cursor.Location.IsInSystemHeader)
            {
                if (!this.parseSystemIncludes)
                    return CXChildVisitResult.CXChildVisit_Continue;
                this.m_context.currentRootContainer = this.m_context.systemRootContainerContext;
            }
            else
            {
                this.m_context.currentRootContainer = this.m_context.userRootContainerContext;
            }
        }

        if (this.m_context.currentRootContainer is null)
        {
            this.rootCompilation.diagnostics.Error($"Unexpected error with cursor location. Cannot determine Root Compilation context.");
            return CXChildVisitResult.CXChildVisit_Continue;
        }

        var visitor = this.m_context.memberVisitors.GetVisitor(cursor.Kind);
        if (visitor != null)
        {
            element = visitor.Visit(this.m_context, cursor, parent);
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

        if (element.sourceFile is null || cursor.IsCursorDefinition(element))
        {
            element.AssignSourceSpan(cursor);
        }

        if (element is ICppDeclaration cppDeclaration && this.parseCommentsEnabled)
        {
            cppDeclaration.comment = cursor.GetComment();
            if (cppDeclaration is ICppAttributeContainer attrContainer && this.parseCommentAttributeEnabled)
            {
                cppDeclaration.comment?.TryToParseAttributes(attrContainer);
            }
        }

        element.ConvertToMetaAttributes();
        return visitor!.visitResult;
    }
}
