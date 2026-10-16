using BGCS.Contracts.Inputs;
namespace BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;

using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Types;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>FunctionDeclVisitor</c>.
/// </summary>
internal unsafe class FunctionDeclVisitor : MemberVisitor
{
    /// <summary>
    /// Gets <c>Kinds</c>.
    /// </summary>
    public override IEnumerable<CXCursorKind> kinds { get; } = [CXCursorKind.CXCursor_FunctionTemplate, CXCursorKind.CXCursor_FunctionDecl, CXCursorKind.CXCursor_Constructor, CXCursorKind.CXCursor_Destructor, CXCursorKind.CXCursor_CXXMethod, CXCursorKind.CXCursor_ConversionFunction, CXCursorKind.CXCursor_ObjCClassMethodDecl, CXCursorKind.CXCursor_ObjCInstanceMethodDecl];

    protected override CppElement? VisitCore(
        CXCursor cursor,
        CXCursor parent
    ) {
        var contextContainer = this.context.GetOrCreateDeclContainer(cursor.SemanticParent);
        var container = contextContainer.declarationContainer;
        if (container == null)
        {
            this.builder.WarningUnhandled(cursor, parent);
            return null;
        }

        var cppClass = container as CppClass;
        var functionName = CXUtil.GetCursorSpelling(cursor);
        //We need ignore the function define out in the class definition here(Otherwise it will has two same functions here~)!
        var semKind = cursor.SemanticParent.Kind;
        if ((semKind == CXCursorKind.CXCursor_StructDecl || semKind == CXCursorKind.CXCursor_ClassDecl || semKind == CXCursorKind.CXCursor_ClassTemplate)
            && cursor.LexicalParent != cursor.SemanticParent
            && cppClass?.templateKind != BGCS.Frontend.Clang.Model.Templates.CppTemplateKind.TemplateSpecializedClass)
        {
            return null;
        }

        var cppFunction = new CppFunction(cursor, functionName)
        {
            visibility = cursor.GetVisibility(),
            storageQualifier = cursor.GetStorageQualifier(),
            linkageKind = cursor.GetLinkageKind(),
            isExternC = this.builder.parserKind == SourceLanguage.C || cursor.IsExternC(parent),
        };
        if (cursor.Kind == CXCursorKind.CXCursor_Constructor)
        {
            Debug.Assert(cppClass != null);
            cppFunction.isConstructor = true;
            cppClass.constructors.Add(cppFunction);
        }
        else if (cursor.Kind == CXCursorKind.CXCursor_Destructor)
        {
            Debug.Assert(cppClass != null);
            cppFunction.isDestructor = true;
            cppClass.destructors.Add(cppFunction);
        }
        else
        {
            container.functions.Add(cppFunction);
        }

        switch (cursor.Kind)
        {
            case CXCursorKind.CXCursor_FunctionTemplate:
                ParseFuncTemplateParams(cursor, cppFunction);
                break;
            case CXCursorKind.CXCursor_ObjCInstanceMethodDecl:
            case CXCursorKind.CXCursor_CXXMethod:
            case CXCursorKind.CXCursor_ConversionFunction:
                cppFunction.flags |= CppFunctionFlags.Method;
                break;
            case CXCursorKind.CXCursor_ObjCClassMethodDecl:
                cppFunction.flags |= CppFunctionFlags.ClassMethod;
                break;
            case CXCursorKind.CXCursor_Constructor:
                cppFunction.flags |= CppFunctionFlags.Constructor;
                break;
            case CXCursorKind.CXCursor_Destructor:
                cppFunction.flags |= CppFunctionFlags.Destructor;
                break;
        }

        if (cursor.IsFunctionInlined)
        {
            cppFunction.flags |= CppFunctionFlags.Inline;
        }

        if (cursor.IsVariadic)
        {
            cppFunction.flags |= CppFunctionFlags.Variadic;
        }

        if (cursor.CXXMethod_IsConst)
        {
            cppFunction.flags |= CppFunctionFlags.Const;
        }

        if (cursor.CXXMethod_IsDefaulted)
        {
            cppFunction.flags |= CppFunctionFlags.Defaulted;
        }

        if (cursor.CXXMethod_IsVirtual)
        {
            cppFunction.flags |= CppFunctionFlags.Virtual;
        }

        if (cursor.CXXMethod_IsPureVirtual)
        {
            cppFunction.flags |= CppFunctionFlags.Pure | CppFunctionFlags.Virtual;
        }

        if (clang.CXXMethod_isDeleted(cursor) != 0)
        {
            cppFunction.flags |= CppFunctionFlags.Deleted;
        }

        // Gets the return type
        var returnType = this.builder.GetCppType(cursor.ResultType.Declaration, cursor.ResultType, cursor);
        if (cppClass != null && cppClass.classKind == CppClassKind.ObjCInterface)
        {
            if (returnType is CppTypedef typedef && typedef.name == "instancetype")
            {
                returnType = new CppPointerType(cursor, cppClass, this.builder.rootCompilation.pointerSize);
            }
        }

        cppFunction.returnType = returnType;
        this.builder.ParseAttributes(cursor, cppFunction, true);
        cppFunction.callingConvention = cursor.Type.GetCallingConvention();
        int i = 0;
        var ctx = (cppFunction, this.builder, i);
        cursor.VisitChildren(static (
            argCursor,
            functionCursor,
            clientData
        ) => {
            ref var ctx = ref Unsafe.AsRef<(CppFunction, CppModelBuilder, int)>(clientData);
            var (cppFunction, Builder, _) = ctx;
            switch (argCursor.Kind)
            {
                case CXCursorKind.CXCursor_ParmDecl:
                    var argName = CXUtil.GetCursorSpelling(argCursor);
                    CppParameter parameter = new(argCursor, Builder.GetCppType(argCursor.Type.Declaration, argCursor.Type, argCursor), argName);
                    cppFunction.parameters.Add(parameter);
                    // Visit default parameter value
                    Builder.VisitInitValue(argCursor, out var paramExpr, out var paramValue);
                    parameter.initValue = paramValue;
                    parameter.initExpression = paramExpr;
                    ctx.Item3++;
                    break;
                // Don't generate a warning for unsupported cursor
                default:
                    //// Attributes should be parsed by ParseAttributes()
                    //if (!(argCursor.Kind >= CXCursorKind.CXCursor_FirstAttr && argCursor.Kind <= CXCursorKind.CXCursor_LastAttr))
                    //{
                    //    WarningUnhandled(cursor, parent);
                    //}
                    break;
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, (CXClientData)Unsafe.AsPointer(ref ctx));
        int parameterCount = clang.Cursor_getNumArguments(cursor);
        if (parameterCount >= 0)
        {
            List<CppParameter> recovered = new(cppFunction.parameters);
            cppFunction.parameters.Clear();
            for (uint index = 0; index < parameterCount; index++)
            {
                CXCursor argument = clang.Cursor_getArgument(cursor, index);
                CppParameter? parameter = recovered.Find(candidate => candidate.cursor == argument);
                if (parameter is null)
                {
                    parameter = new(argument, this.builder.GetCppType(argument.Type.Declaration, argument.Type, argument), CXUtil.GetCursorSpelling(argument));
                    this.builder.VisitInitValue(argument, out var expression, out var value);
                    parameter.initExpression = expression;
                    parameter.initValue = value;
                }
                CXType written = argument.Type.CanonicalType;
                if (written.kind is CXTypeKind.CXType_ConstantArray or CXTypeKind.CXType_IncompleteArray or CXTypeKind.CXType_VariableArray
                    or CXTypeKind.CXType_DependentSizedArray or CXTypeKind.CXType_FunctionProto or CXTypeKind.CXType_FunctionNoProto)
                {
                    CXType adjusted = cursor.Type.CanonicalType.GetArgType(index);
                    if (adjusted.kind != CXTypeKind.CXType_Pointer)
                        throw new InvalidOperationException("The native compiler did not provide the adjusted pointer ABI for an array or function parameter.");
                    parameter.type = this.builder.GetCppType(adjusted.Declaration, adjusted, argument);
                }
                cppFunction.parameters.Add(parameter);
            }
        }
        return cppFunction;
    }

    private void ParseFuncTemplateParams(
        CXCursor cursor,
        CppFunction cppFunction
    ) {
        var ctx = (cppFunction, this.builder);
        cppFunction.flags |= CppFunctionFlags.FunctionTemplate;
        cursor.VisitChildren(static (
            childCursor,
            funcCursor,
            clientData
        ) => {
            var (cppFunction, Builder) = Unsafe.AsRef<(CppFunction, CppModelBuilder)>(clientData);
            var param = Builder.TryToCreateTemplateParameters(childCursor);
            if (param != null)
            {
                cppFunction.templateParameters.Add(param);
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, (CXClientData)Unsafe.AsPointer(ref ctx));
    }
}
