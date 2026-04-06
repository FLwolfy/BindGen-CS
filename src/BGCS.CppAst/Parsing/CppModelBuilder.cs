using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using System.Runtime.CompilerServices;

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Internal class used to build the entire C++ model from the libclang representation.
/// </summary>
public partial class CppModelBuilder : CompilationLoggerBase
{
    private readonly CppModelContext context;

    /// <summary>
    /// Initializes a new instance of <see cref="CppModelBuilder"/>.
    /// </summary>
    public CppModelBuilder(CXTranslationUnit translationUnit)
    {
        context = new CppModelContext(this, translationUnit);
    }

    /// <summary>
    /// Gets or sets <c>AutoSquashTypedef</c>.
    /// </summary>
    public bool AutoSquashTypedef { get; set; }

    /// <summary>
    /// Gets or sets <c>ParseSystemIncludes</c>.
    /// </summary>
    public bool ParseSystemIncludes { get; set; }

    /// <summary>
    /// Gets or sets <c>ParseTokenAttributeEnabled</c>.
    /// </summary>
    public bool ParseTokenAttributeEnabled { get; set; }

    /// <summary>
    /// Gets or sets <c>ParseCommentAttributeEnabled</c>.
    /// </summary>
    public bool ParseCommentAttributeEnabled { get; set; }

    /// <summary>
    /// Exposes public member <c>context.RootCompilation</c>.
    /// </summary>
    public override CppCompilation RootCompilation => context.RootCompilation;

    /// <summary>
    /// Exposes public member <c>context.Containers</c>.
    /// </summary>
    public Dictionary<CursorKey, CppContainerContext> Containers => context.Containers;

    /// <summary>
    /// Exposes public member <c>context.CurrentRootContainer</c>.
    /// </summary>
    public CppContainerContext CurrentRootContainer => context.CurrentRootContainer;

    /// <summary>
    /// Exposes public member <c>context.TypedefResolver</c>.
    /// </summary>
    public TypedefResolver TypedefResolver => context.TypedefResolver;

    /// <summary>
    /// Executes public operation <c>TryToCreateTemplateParameters</c>.
    /// </summary>
    public CppType? TryToCreateTemplateParameters(CXCursor cursor)
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
                    // TODO: add template template parameter support here
                    RootCompilation.Diagnostics.Warning($"Unhandled template parameter: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                    var templateParameterName = CXUtil.GetCursorSpelling(cursor);
                    CppTemplateParameterType templateParameterType = new(cursor, templateParameterName);
                    return templateParameterType;
                }
        }

        return null;
    }

    /// <summary>
    /// Executes public operation <c>VisitTranslationUnit</c>.
    /// </summary>
    public unsafe CXChildVisitResult VisitTranslationUnit(CXCursor cursor, CXCursor parent, void* data)
    {
        var result = VisitMember(cursor, parent, data);
        return result;
    }

    /// <summary>
    /// Executes public operation <c>VisitInitValue</c>.
    /// </summary>
    public unsafe void VisitInitValue(CXCursor cursor, out CppExpression? expression, out CppValue? value)
    {
        expression = null;
        cursor.VisitChildren(static (initCursor, varCursor, clientData) =>
        {
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
                RootCompilation.Diagnostics.Warning($"Not supported field default value {CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                break;
        }
    }
}
