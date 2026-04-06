namespace BGCS.CppAst.Parsing.Visitors;
using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Defines the public class <c>ClassStructDeclVisitor</c>.
/// </summary>
public class ClassStructDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> Kinds { get; } =
    [
        CXCursorKind.CXCursor_ClassTemplate,
        CXCursorKind.CXCursor_ClassTemplatePartialSpecialization,
        CXCursorKind.CXCursor_ClassDecl,
        CXCursorKind.CXCursor_StructDecl,
        CXCursorKind.CXCursor_UnionDecl,
        CXCursorKind.CXCursor_ObjCInterfaceDecl,
        CXCursorKind.CXCursor_ObjCProtocolDecl,
        CXCursorKind.CXCursor_ObjCCategoryDecl,
    ];

    protected override unsafe CppContainerContext VisitCore(CXCursor cursor, CXCursor parent)
    {
        ICppDeclarationContainer parentContainer = Context.GetOrCreateDeclContainer(cursor.SemanticParent).DeclarationContainer;

        CppClass cppClass = new(cursor, CXUtil.GetCursorSpelling(cursor));
        parentContainer.Classes.Add(cppClass);
        cppClass.IsAnonymous = cursor.IsAnonymous;
        switch (cursor.Kind)
        {
            case CXCursorKind.CXCursor_ClassDecl:
            case CXCursorKind.CXCursor_ClassTemplate:
            case CXCursorKind.CXCursor_ClassTemplatePartialSpecialization:
                cppClass.ClassKind = CppClassKind.Class;
                break;

            case CXCursorKind.CXCursor_StructDecl:
                cppClass.ClassKind = CppClassKind.Struct;
                break;

            case CXCursorKind.CXCursor_UnionDecl:
                cppClass.ClassKind = CppClassKind.Union;
                break;

            case CXCursorKind.CXCursor_ObjCInterfaceDecl:
                cppClass.ClassKind = CppClassKind.ObjCInterface;
                break;

            case CXCursorKind.CXCursor_ObjCProtocolDecl:
                cppClass.ClassKind = CppClassKind.ObjCProtocol;
                break;

            case CXCursorKind.CXCursor_ObjCCategoryDecl:
                {
                    cppClass.ClassKind = CppClassKind.ObjCInterfaceCategory;

                    // Fetch the target class for the category
                    CXCursor parentCursor = default;
                    cursor.VisitChildren(static (cxCursor, parent, clientData) =>
                    {
                        ref CXCursor parentCursor = ref Unsafe.AsRef<CXCursor>(clientData);
                        if (cxCursor.Kind == CXCursorKind.CXCursor_ObjCClassRef)
                        {
                            parentCursor = cxCursor.Referenced;
                            return CXChildVisitResult.CXChildVisit_Break;
                        }

                        return CXChildVisitResult.CXChildVisit_Continue;
                    }, (CXClientData)Unsafe.AsPointer(ref parentCursor));

                    var parentClassContainer = Context.GetOrCreateDeclContainer(parentCursor).Container;
                    var targetClass = (CppClass)parentClassContainer;
                    cppClass.ObjCCategoryName = cppClass.Name;
                    cppClass.Name = targetClass.Name;
                    cppClass.ObjCCategoryTargetClass = targetClass;

                    // Link back
                    targetClass.ObjCCategories.Add(cppClass);
                    break;
                }
        }

        cppClass.IsAbstract = cursor.CXXRecord_IsAbstract;
        cppClass.IsCompleteDefinition = cursor.IsCompleteDefinition;
        cppClass.IsDefined = cursor.IsDefined;
        cppClass.IsPODType = cursor.Type.IsPODType;

        if (cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplateSpecialization
            || cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplatePartialSpecialization)
        {
            //Try to generate template class first
            cppClass.SpecializedTemplate = (CppClass)Context.GetOrCreateDeclContainer(cursor.SpecializedCursorTemplate).Container;
            if (cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplatePartialSpecialization)
            {
                cppClass.TemplateKind = CppTemplateKind.PartialTemplateClass;
            }
            else
            {
                cppClass.TemplateKind = CppTemplateKind.TemplateSpecializedClass;
            }

            // Just use low level api to call ClangSharp
            var tempArgsCount = cursor.NumTemplateArguments;
            var tempParams = cppClass.SpecializedTemplate.TemplateParameters;

            // Just use template class template params here
            for (uint i = 0; i < tempParams.Count; i++)
            {
                var param = tempParams[(int)i];
                var templateArgument = cursor.GetTemplateArgument(i);

                switch (param)
                {
                    case CppTemplateParameterType paramType:
                        cppClass.TemplateParameters.Add(new CppTemplateParameterType(templateArgument, paramType.Name));
                        break;

                    case CppTemplateParameterNonType nonType:
                        cppClass.TemplateParameters.Add(new CppTemplateParameterNonType(templateArgument, nonType.Name, nonType.NoneTemplateType));
                        break;
                }
            }

            if (cppClass.TemplateKind == CppTemplateKind.TemplateSpecializedClass)
            {
                Debug.Assert(cppClass.SpecializedTemplate.TemplateParameters.Count == tempArgsCount);
            }

            for (uint i = 0; i < tempArgsCount; i++)
            {
                var arg = cursor.GetTemplateArgument(i);
                switch (arg.kind)
                {
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                        {
                            var argh = arg.AsType;
                            var argType = Builder.GetCppType(argh.Declaration, argh, cursor);
                            cppClass.TemplateSpecializedArguments.Add(new CppTemplateArgument(arg, tempParams[(int)i], argType, argh.TypeClass != CX_TypeClass.CX_TypeClass_TemplateTypeParm));
                        }
                        break;

                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                        {
                            cppClass.TemplateSpecializedArguments.Add(new CppTemplateArgument(arg, tempParams[(int)i], arg.AsIntegral));
                        }
                        break;

                    default:
                        {
                            RootCompilation.Diagnostics.Warning($"Unhandled template argument with type {arg.kind}: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                            cppClass.TemplateSpecializedArguments.Add(new CppTemplateArgument(arg, tempParams[(int)i], arg.ToString()));
                        }
                        break;
                }
                arg.Dispose();
            }
        }
        else
        {
            AddTemplateParameters(cursor, cppClass);
        }

        var visibility = cursor.Kind == CXCursorKind.CXCursor_ClassDecl ? CppVisibility.Private : CppVisibility.Public;

        return new(cppClass, visibility);
    }

    /// <summary>
    /// Adds data or behavior through <c>AddTemplateParameters</c>.
    /// </summary>
    public unsafe void AddTemplateParameters(CXCursor cursor, CppClass cppClass)
    {
        var ctx = (cppClass, Context);
        cursor.VisitChildren(static (childCursor, classCursor, clientData) =>
        {
            var (cppClass, context) = Unsafe.AsRef<(CppClass, CppModelContext)>(clientData);
            var builder = context.Builder;

            if (cppClass.ClassKind == CppClassKind.ObjCInterface ||
                cppClass.ClassKind == CppClassKind.ObjCProtocol)
            {
                var param = context.TryToCreateTemplateParametersObjC(childCursor);
                if (param != null)
                {
                    cppClass.TemplateKind = CppTemplateKind.ObjCGenericClass;
                    cppClass.TemplateParameters.Add(param);
                }
            }
            else
            {
                var param = builder.TryToCreateTemplateParameters(childCursor);
                if (param != null)
                {
                    cppClass.TemplateKind = CppTemplateKind.TemplateClass;
                    cppClass.TemplateParameters.Add(param);
                }
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, (CXClientData)Unsafe.AsPointer(ref ctx));
    }
}
