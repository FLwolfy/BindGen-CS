using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Internal class used to build the entire C++ model from the libclang representation.
/// </summary>
internal partial class CppModelBuilder : CompilationLoggerBase
{
    private readonly CppModelContext m_context;
    /// <summary>
    /// Initializes a new instance of <see cref = "CppModelBuilder"/>.
    /// </summary>
    public CppModelBuilder(CXTranslationUnit translationUnit)
    {
        this.m_context = new CppModelContext(this, translationUnit);
    }

    /// <summary>
    /// Gets or sets <c>AutoSquashTypedef</c>.
    /// </summary>
    public bool autoSquashTypedef { get; set; }
    /// <summary>
    /// Gets or sets the language mode used for parsing the translation unit.
    /// </summary>
    public CppParserKind parserKind { get; set; } = CppParserKind.Cpp;
    /// <summary>
    /// Gets or sets <c>ParseSystemIncludes</c>.
    /// </summary>
    public bool parseSystemIncludes { get; set; }
    /// <summary>
    /// Gets or sets whether declaration comments are parsed into the AST.
    /// </summary>
    public bool parseCommentsEnabled { get; set; } = true;
    /// <summary>
    /// Gets or sets <c>ParseTokenAttributeEnabled</c>.
    /// </summary>
    public bool parseTokenAttributeEnabled { get; set; }
    /// <summary>
    /// Gets or sets <c>ParseCommentAttributeEnabled</c>.
    /// </summary>
    public bool parseCommentAttributeEnabled { get; set; }
    /// <summary>
    /// Exposes public member <c>context.RootCompilation</c>.
    /// </summary>
    public override CppCompilation rootCompilation => this.m_context.rootCompilation;
    /// <summary>
    /// Exposes public member <c>context.Containers</c>.
    /// </summary>
    public Dictionary<CursorKey, CppContainerContext> containers => this.m_context.containers;
    /// <summary>
    /// Exposes public member <c>context.CurrentRootContainer</c>.
    /// </summary>
    public CppContainerContext currentRootContainer => this.m_context.currentRootContainer;
    /// <summary>
    /// Exposes public member <c>context.TypedefResolver</c>.
    /// </summary>
    public TypedefResolver typedefResolver => this.m_context.typedefResolver;

    /// <summary>
    /// Executes public operation <c>TryToCreateTemplateParameters</c>.
    /// </summary>
    public unsafe CppType? TryToCreateTemplateParameters(CXCursor cursor)
    {
        switch (cursor.Kind)
        {
            case CXCursorKind.CXCursor_TemplateTypeParameter:
                {
                    var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                    CppTemplateParameterType templateParameterType = new(cursor, templateParameterName);
                    return templateParameterType;
                }

            case CXCursorKind.CXCursor_NonTypeTemplateParameter:
                {
                    var type = cursor.Type;
                    var cppType = GetCppType(type.Declaration, type, cursor);
                    var name = CXUtil.GetCursorSpelling(cursor);
                    CppTemplateParameterNonType templateParameterType = new(cursor, name, cppType);
                    return templateParameterType;
                }

            case CXCursorKind.CXCursor_TemplateTemplateParameter:
                {
                    var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                    CppTemplateParameterTemplate parameter = new(cursor, templateParameterName);
                    var state = (parameter, this);
                    cursor.VisitChildren(static (
                        child,
                        _,
                        data
                    ) => {
                        var (owner, builder) = Unsafe.AsRef<(CppTemplateParameterTemplate, CppModelBuilder)>(data);
                        CppType? nested = builder.TryToCreateTemplateParameters(child);
                        if (nested != null)
                            owner.parameters.Add(nested);
                        return CXChildVisitResult.CXChildVisit_Continue;
                    }, (CXClientData)Unsafe.AsPointer(ref state));
                    return parameter;
                }
        }

        return null;
    }

    /// <summary>
    /// Executes public operation <c>VisitTranslationUnit</c>.
    /// </summary>
    public unsafe CXChildVisitResult VisitTranslationUnit(
        CXCursor cursor,
        CXCursor parent,
        void* data
    ) {
        var result = VisitMember(cursor, parent, data);
        return result;
    }

    /// <summary>
    /// Executes public operation <c>VisitInitValue</c>.
    /// </summary>
    public unsafe void VisitInitValue(
        CXCursor cursor,
        out CppExpression? expression,
        out CppValue? value
    ) {
        expression = null;
        cursor.VisitChildren(static (
            initCursor,
            varCursor,
            clientData
        ) => {
            ref CppExpression? expression = ref Unsafe.AsRef<CppExpression?>(clientData);
            if (initCursor.IsExpression())
            {
                expression = VisitExpression(initCursor);
                return CXChildVisitResult.CXChildVisit_Break;
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, (CXClientData)Unsafe.AsPointer(ref expression));
        using CXEvalResult resultEval = cursor.Evaluate;
        switch (resultEval.Kind)
        {
            case CXEvalResultKind.CXEval_Int:
                value = new(cursor, resultEval.AsLongLong);
                break;
            case CXEvalResultKind.CXEval_Float:
                value = new(cursor, resultEval.AsDouble);
                break;
            case CXEvalResultKind.CXEval_ObjCStrLiteral:
            case CXEvalResultKind.CXEval_StrLiteral:
            case CXEvalResultKind.CXEval_CFStr:
                value = new(cursor, resultEval.AsStr);
                break;
            case CXEvalResultKind.CXEval_UnExposed:
                value = null;
                break;
            default:
                value = null;
                this.rootCompilation.diagnostics.Warning($"Not supported field default value {CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                break;
        }
    }
}
