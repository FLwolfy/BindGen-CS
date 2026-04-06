namespace BGCS.CppAst.Parsing;
using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using BGCS.CppAst.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Defines the public class <c>CppModelBuilder</c>.
/// </summary>
public unsafe partial class CppModelBuilder
{
    /// <summary>
    /// Returns computed data from <c>GetCppType</c>.
    /// </summary>
    public CppType GetCppType(CXCursor cursor, CXType type, CXCursor parent)
    {
        var cppType = GetCppTypeInternal(cursor, type, parent);

        if (type.IsConstQualified)
        {
            // Skip if it is already qualified.
            if (cppType is CppUnexposedType || cppType is CppQualifiedType q && q.Qualifier == CppTypeQualifier.Const)
            {
                return cppType;
            }

            return new CppQualifiedType(cursor, CppTypeQualifier.Const, cppType);
        }
        else if (type.IsVolatileQualified)
        {
            // Skip if it is already qualified.
            if (cppType is CppQualifiedType q && q.Qualifier == CppTypeQualifier.Volatile)
            {
                return cppType;
            }

            return new CppQualifiedType(cursor, CppTypeQualifier.Volatile, cppType);
        }

        return cppType;
    }

    private CppType GetCppTypeInternal(CXCursor cursor, CXType type, CXCursor parent)
    {
        if (CppPrimitiveType.KindToPrimitive.TryGetValue(type.kind, out var primitiveType))
        {
            return primitiveType;
        }

        switch (type.kind)
        {
            case CXTypeKind.CXType_ObjCObjectPointer:
            case CXTypeKind.CXType_Pointer:
                return new CppPointerType(cursor, GetCppType(type.PointeeType.Declaration, type.PointeeType, parent)) { SizeOf = (int)type.SizeOf };

            case CXTypeKind.CXType_LValueReference:
                return new CppReferenceType(cursor, GetCppType(type.PointeeType.Declaration, type.PointeeType, parent));

            case CXTypeKind.CXType_Record:
                return VisitClassDecl(cursor);

            case CXTypeKind.CXType_ObjCInterface:
                return VisitClassDecl(cursor);

            case CXTypeKind.CXType_Enum:
                return (CppType)MemberVisitorRegistry.GetVisitor<EnumDeclMemberVisitor>().Visit(context, cursor, parent)!;

            case CXTypeKind.CXType_FunctionProto:
                return VisitFunctionType(cursor, type, parent);

            case CXTypeKind.CXType_BlockPointer:
                return VisitFunctionType(cursor, type.PointeeType, parent, true);

            case CXTypeKind.CXType_Typedef:
                return (CppType)MemberVisitorRegistry.GetVisitor<TypedefDeclVisitor>().Visit(context, cursor, parent)!;

            case CXTypeKind.CXType_Elaborated:
                return VisitElaboratedDecl(cursor, type, parent);

            case CXTypeKind.CXType_ConstantArray:
            case CXTypeKind.CXType_IncompleteArray:
                {
                    var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent);
                    return new CppArrayType(cursor, elementType, (int)type.ArraySize);
                }

            case CXTypeKind.CXType_DependentSizedArray:
                {
                    // TODO: this is not yet supported
                    RootCompilation.Diagnostics.Warning($"Dependent sized arrays `{CXUtil.GetTypeSpelling(type)}` from `{CXUtil.GetCursorSpelling(parent)}` is not supported", parent.GetSourceLocation());
                    var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent);
                    return new CppArrayType(cursor, elementType, (int)type.ArraySize);
                }

            case CXTypeKind.CXType_Unexposed:
                {
                    // It may be possible to parse them even if they are unexposed.
                    var kind = type.Declaration.Type.kind;
                    if (kind != CXTypeKind.CXType_Unexposed && kind != CXTypeKind.CXType_Invalid)
                    {
                        return GetCppType(type.Declaration, type.Declaration.Type, parent);
                    }

                    CppUnexposedType cppUnexposedType = new(cursor, CXUtil.GetTypeSpelling(type)) { SizeOf = (int)type.SizeOf };
                    var templateParameters = ParseTemplateSpecializedArguments(cursor, type);
                    if (templateParameters != null)
                    {
                        cppUnexposedType.TemplateParameters.AddRange(templateParameters);
                    }
                    return cppUnexposedType;
                }

            case CXTypeKind.CXType_Attributed:
                return GetCppType(type.ModifiedType.Declaration, type.ModifiedType, parent);

            case CXTypeKind.CXType_Auto:
                return GetCppType(type.Declaration, type.Declaration.Type, parent);

            case CXTypeKind.CXType_ObjCTypeParam:
                {
                    CppTemplateParameterType templateArgType = context.TryToCreateTemplateParametersObjC(cursor);

                    // Record that a typedef is using a template parameter type
                    // which will require to re-parent the typedef to the Obj-C interface it belongs to
                    if (context.CurrentTypedefKey != default)
                    {
                        var map = context.MapTemplateParameterTypeToTypedefKeys;
                        if (!map.TryGetValue(templateArgType, out var typedefKeys))
                        {
                            typedefKeys = [];
                            map.Add(templateArgType, typedefKeys);
                        }

                        typedefKeys.Add(context.CurrentTypedefKey);
                    }

                    return templateArgType;
                }

            default:
                {
                    WarningUnhandled(cursor, parent, type);
                    return new CppUnexposedType(cursor, CXUtil.GetTypeSpelling(type)) { SizeOf = (int)type.SizeOf };
                }
        }
    }

    private struct VisitedFunctionTypeContext
    {
        /// <summary>
        /// Exposes public member <c>Builder</c>.
        /// </summary>
        public CppModelBuilder Builder;
        /// <summary>
        /// Exposes public member <c>CppFunction</c>.
        /// </summary>
        public CppFunctionTypeBase CppFunction;
        /// <summary>
        /// Exposes public member <c>IsParsingParameter</c>.
        /// </summary>
        public bool IsParsingParameter;

        /// <summary>
        /// Initializes a new instance of <see cref="VisitedFunctionTypeContext"/>.
        /// </summary>
        public VisitedFunctionTypeContext(CppModelBuilder builder, CppFunctionTypeBase cppFunction)
        {
            Builder = builder;
            CppFunction = cppFunction;
        }
    }

    private CppFunctionTypeBase VisitFunctionType(CXCursor cursor, CXType type, CXCursor parent, bool isBlockFunctionType = false)
    {
        // Gets the return type
        var returnType = GetCppType(type.ResultType.Declaration, type.ResultType, cursor);

        var cppFunction = isBlockFunctionType
            ? (CppFunctionTypeBase)new CppBlockFunctionType(cursor, returnType)
            : new CppFunctionType(cursor, returnType);
        cppFunction.CallingConvention = type.GetCallingConvention();

        // We don't use this but use the visitor children to try to recover the parameter names

        //            for (uint i = 0; i < type.NumArgTypes; i++)
        //            {
        //                var argType = type.GetArgType(i);
        //                var cppType = GetCppType(argType.Declaration, argType, type.Declaration, data);
        //                cppFunction.ParameterTypes.Add(cppType);
        //            }

        VisitedFunctionTypeContext ctx = new(this, cppFunction);
        parent.VisitChildren(static (argCursor, functionCursor, clientData) =>
        {
            ref var ctx = ref Unsafe.AsRef<VisitedFunctionTypeContext>(clientData);
            var builder = ctx.Builder;
            var cppFunction = ctx.CppFunction;

            if (argCursor.Kind == CXCursorKind.CXCursor_ParmDecl)
            {
                var name = CXUtil.GetCursorSpelling(argCursor);
                var parameterType = builder.GetCppType(argCursor.Type.Declaration, argCursor.Type, argCursor);

                cppFunction.Parameters.Add(new CppParameter(argCursor, parameterType, name));
                ctx.IsParsingParameter = true;
            }
            return ctx.IsParsingParameter ? CXChildVisitResult.CXChildVisit_Continue : CXChildVisitResult.CXChildVisit_Recurse;
        }, (CXClientData)Unsafe.AsPointer(ref ctx));

        return cppFunction;
    }

    private List<CppType>? ParseTemplateSpecializedArguments(CXCursor cursor, CXType type)
    {
        var numTemplateArguments = type.NumTemplateArguments;
        if (numTemplateArguments < 0) return null;

        List<CppType> templateCppTypes = [];
        for (var templateIndex = 0; templateIndex < numTemplateArguments; ++templateIndex)
        {
            var templateArg = type.GetTemplateArgument((uint)templateIndex);

            switch (templateArg.kind)
            {
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                    var templateArgType = templateArg.AsType;
                    //var templateArg = type.GetTemplateArgumentAsType((uint)templateIndex);
                    var templateCppType = GetCppType(templateArgType.Declaration, templateArgType, cursor);
                    templateCppTypes.Add(templateCppType);
                    break;

                case CXTemplateArgumentKind.CXTemplateArgumentKind_Null:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Declaration:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_NullPtr:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Template:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_TemplateExpansion:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Expression:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Pack:
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Invalid:
                    break;

                default:
                    throw new InvalidOperationException();
            }
        }

        return templateCppTypes;
    }

    /// <summary>
    /// Executes public operation <c>VisitClassDecl</c>.
    /// </summary>
    public unsafe CppClass VisitClassDecl(CXCursor cursor)
    {
        var cppStruct = this.context.GetOrCreateDeclContainer<CppClass>(cursor, out var context);
        if (cursor.Definition.Kind != CXCursorKind.CXCursor_FirstInvalid && cursor != cursor.Definition)
        {
            var definition = VisitClassDecl(cursor.Definition);
            cppStruct.Definition = definition;
        }
        if (cursor.IsCursorDefinition(cppStruct) && !context.IsChildrenVisited)
        {
            ParseAttributes(cursor, cppStruct, false);
            cppStruct.IsDefinition = true;
            cppStruct.SizeOf = (int)cursor.Type.SizeOf;
            cppStruct.AlignOf = (int)cursor.Type.AlignOf;
            context.IsChildrenVisited = true;
            var saveCurrentClassBeingVisited = this.context.CurrentClassBeingVisited;
            this.context.CurrentClassBeingVisited = cppStruct;
            cursor.VisitChildren(VisitMember, default);

            if (cppStruct.Properties.Count > 0)
            {
                foreach (var prop in cppStruct.Properties)
                {
                    prop.Getter = cppStruct.Functions.FindElementByName(prop.GetterName);
                    prop.Setter = cppStruct.Functions.FindElementByName(prop.SetterName);
                }
            }

            cppStruct.AssignSourceSpan(cursor);

            this.context.CurrentClassBeingVisited = saveCurrentClassBeingVisited;
        }
        return cppStruct;
    }

    private CppType VisitElaboratedDecl(CXCursor cursor, CXType type, CXCursor parent)
    {
        var key = context.GetCursorKey(cursor);
        if (TypedefResolver.TryResolve(key, out var typeRef))
        {
            return typeRef;
        }

        // If the type has been already declared, return it immediately.
        if (Containers.TryGetValue(key, out var containerContext))
        {
            return (CppType)containerContext.Container;
        }

        // TODO: Pseudo fix, we are not supposed to land here, as the TryGet before should resolve an existing type already declared (but not necessarily defined)
        return GetCppType(type.CanonicalType.Declaration, type.CanonicalType, parent);
    }
}
