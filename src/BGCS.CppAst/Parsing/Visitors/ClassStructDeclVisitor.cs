namespace BGCS.CppAst.Parsing.Visitors;

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>ClassStructDeclVisitor</c>.
/// </summary>
internal class ClassStructDeclVisitor : DeclContainerVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_ClassTemplate, CXCursorKind.CXCursor_ClassTemplatePartialSpecialization, CXCursorKind.CXCursor_ClassDecl, CXCursorKind.CXCursor_StructDecl, CXCursorKind.CXCursor_UnionDecl, CXCursorKind.CXCursor_ObjCInterfaceDecl, CXCursorKind.CXCursor_ObjCProtocolDecl, CXCursorKind.CXCursor_ObjCCategoryDecl,];

    protected override unsafe CppContainerContext VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        ICppDeclarationContainer parentContainer = this.context.GetOrCreateDeclContainer(cursor.SemanticParent).declarationContainer;
        CppClass cppClass = new(cursor, CXUtil.GetCursorSpelling(cursor));
        parentContainer.classes.Add(cppClass);
        cppClass.isAnonymous = cursor.IsAnonymous;
        switch (cursor.Kind)
        {
            case CXCursorKind.CXCursor_ClassDecl:
            case CXCursorKind.CXCursor_ClassTemplate:
            case CXCursorKind.CXCursor_ClassTemplatePartialSpecialization:
                cppClass.classKind = CppClassKind.Class;
                break;
            case CXCursorKind.CXCursor_StructDecl:
                cppClass.classKind = CppClassKind.Struct;
                break;
            case CXCursorKind.CXCursor_UnionDecl:
                cppClass.classKind = CppClassKind.Union;
                break;
            case CXCursorKind.CXCursor_ObjCInterfaceDecl:
                cppClass.classKind = CppClassKind.ObjCInterface;
                break;
            case CXCursorKind.CXCursor_ObjCProtocolDecl:
                cppClass.classKind = CppClassKind.ObjCProtocol;
                break;
            case CXCursorKind.CXCursor_ObjCCategoryDecl:
                {
                    cppClass.classKind = CppClassKind.ObjCInterfaceCategory;
                    // Fetch the target class for the category
                    CXCursor parentCursor = default;
                    cursor.VisitChildren(static (
                        cxCursor,
                        parent,
                        clientData
                    ) => {
                        ref CXCursor parentCursor = ref Unsafe.AsRef<CXCursor>(clientData);
                        if (cxCursor.Kind == CXCursorKind.CXCursor_ObjCClassRef)
                        {
                            parentCursor = cxCursor.Referenced;
                            return CXChildVisitResult.CXChildVisit_Break;
                        }

                        return CXChildVisitResult.CXChildVisit_Continue;
                    }, (CXClientData)Unsafe.AsPointer(ref parentCursor));
                    var parentClassContainer = this.context.GetOrCreateDeclContainer(parentCursor).container;
                    var targetClass = (CppClass)parentClassContainer;
                    cppClass.objCCategoryName = cppClass.name;
                    cppClass.name = targetClass.name;
                    cppClass.objCCategoryTargetClass = targetClass;
                    // Link back
                    targetClass.objCCategories.Add(cppClass);
                    break;
                }
        }

        cppClass.isAbstract = cursor.CXXRecord_IsAbstract;
        cppClass.isCompleteDefinition = cursor.IsCompleteDefinition;
        cppClass.isDefined = cursor.IsDefined;
        cppClass.isPODType = cursor.Type.IsPODType;
        if (cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplateSpecialization || cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplatePartialSpecialization)
        {
            //Try to generate template class first
            cppClass.specializedTemplate = (CppClass)this.context.GetOrCreateDeclContainer(cursor.SpecializedCursorTemplate).container;
            if (cursor.DeclKind == CX_DeclKind.CX_DeclKind_ClassTemplatePartialSpecialization)
            {
                cppClass.templateKind = CppTemplateKind.PartialTemplateClass;
            }
            else
            {
                cppClass.templateKind = CppTemplateKind.TemplateSpecializedClass;
            }

            // Just use low level api to call ClangSharp
            var tempArgsCount = cursor.NumTemplateArguments;
            var tempParams = cppClass.specializedTemplate.templateParameters;
            // Just use template class template params here
            for (uint i = 0; i < tempParams.Count; i++)
            {
                var param = tempParams[(int)i];
                var templateArgument = cursor.GetTemplateArgument(i);
                switch (param)
                {
                    case CppTemplateParameterType paramType:
                        cppClass.templateParameters.Add(new CppTemplateParameterType(templateArgument, paramType.name));
                        break;
                    case CppTemplateParameterNonType nonType:
                        cppClass.templateParameters.Add(new CppTemplateParameterNonType(templateArgument, nonType.name, nonType.noneTemplateType));
                        break;
                    case CppTemplateParameterTemplate template:
                        cppClass.templateParameters.Add(new CppTemplateParameterTemplate(templateArgument, template.name, template.parameters));
                        break;
                }
            }

            if (cppClass.templateKind == CppTemplateKind.TemplateSpecializedClass)
            {
                Debug.Assert(cppClass.specializedTemplate.templateParameters.Count == tempArgsCount);
            }

            for (uint i = 0; i < tempArgsCount; i++)
            {
                var arg = cursor.GetTemplateArgument(i);
                switch (arg.kind)
                {
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                        {
                            var argh = arg.AsType;
                            var argType = this.builder.GetCppType(argh.Declaration, argh, cursor);
                            cppClass.templateSpecializedArguments.Add(new CppTemplateArgument(arg, tempParams[(int)i], argType, argh.TypeClass != CX_TypeClass.CX_TypeClass_TemplateTypeParm));
                        }

                        break;
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                        {
                            cppClass.templateSpecializedArguments.Add(new CppTemplateArgument(arg, tempParams[(int)i], arg.AsIntegral));
                        }

                        break;
                    case CXTemplateArgumentKind.CXTemplateArgumentKind_Pack:
                        for (uint packIndex = 0; packIndex < arg.NumPackElements; packIndex++)
                        {
                            using var packedArgument = arg.GetPackElement(packIndex);
                            if (packedArgument.kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
                            {
                                var packedType = packedArgument.AsType;
                                var argumentType = this.builder.GetCppType(packedType.Declaration, packedType, cursor);
                                cppClass.templateSpecializedArguments.Add(new CppTemplateArgument(
                                    packedArgument, tempParams[(int)i], argumentType,
                                    packedType.TypeClass != CX_TypeClass.CX_TypeClass_TemplateTypeParm));
                            }
                            else if (packedArgument.kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Integral)
                            {
                                cppClass.templateSpecializedArguments.Add(new CppTemplateArgument(
                                    packedArgument, tempParams[(int)i], packedArgument.AsIntegral));
                            }
                            else
                            {
                                cppClass.templateSpecializedArguments.Add(new CppTemplateArgument(
                                    packedArgument, tempParams[(int)i], packedArgument.ToString()));
                            }
                        }
                        break;
                    default:
                        {
                            this.rootCompilation.diagnostics.Warning($"Unhandled template argument with type {arg.kind}: {cursor.Kind}/{CXUtil.GetCursorSpelling(cursor)}", cursor.GetSourceLocation());
                            cppClass.templateSpecializedArguments.Add(new CppTemplateArgument(arg, tempParams[(int)i], arg.ToString()));
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
    public unsafe void AddTemplateParameters(
        CXCursor cursor,
        CppClass cppClass
    ) {
        var ctx = (cppClass, this.context);
        cursor.VisitChildren(static (
            childCursor,
            classCursor,
            clientData
        ) => {
            var (cppClass, context) = Unsafe.AsRef<(CppClass, CppModelContext)>(clientData);
            var builder = context.builder;
            if (cppClass.classKind == CppClassKind.ObjCInterface || cppClass.classKind == CppClassKind.ObjCProtocol)
            {
                var param = context.TryToCreateTemplateParametersObjC(childCursor);
                if (param != null)
                {
                    cppClass.templateKind = CppTemplateKind.ObjCGenericClass;
                    cppClass.templateParameters.Add(param);
                }
            }
            else
            {
                var param = builder.TryToCreateTemplateParameters(childCursor);
                if (param != null)
                {
                    cppClass.templateKind = CppTemplateKind.TemplateClass;
                    cppClass.templateParameters.Add(param);
                }
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, (CXClientData)Unsafe.AsPointer(ref ctx));
    }
}
