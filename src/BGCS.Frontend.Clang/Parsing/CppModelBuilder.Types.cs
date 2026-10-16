namespace BGCS.Frontend.Clang.Parsing;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BGCS.Frontend.Clang.Collections;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Templates;
using BGCS.Frontend.Clang.Model.Types;
using BGCS.Frontend.Clang.Parsing.Visitors.MemberVisitors;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

/// <summary>
/// Defines the public class <c>CppModelBuilder</c>.
/// </summary>
internal unsafe partial class CppModelBuilder
{
    /// <summary>
    /// Returns computed data from <c>GetCppType</c>.
    /// </summary>
    public CppType GetCppType(
        CXCursor cursor,
        CXType type,
        CXCursor parent
    ) {
        // The unqualified projection can be shared by many const/volatile
        // uses. Its canonical spelling must describe that base projection;
        // qualifiers are captured on the explicit view wrappers below.
        var cppType = CaptureStorage(GetCppTypeInternal(cursor, type, parent), type.UnqualifiedType);
        if (cppType is CppUnexposedType)
            return cppType;
        if (type.IsVolatileQualified && !(cppType is CppQualifiedType existingVolatile && existingVolatile.qualifier == CppTypeQualifier.Volatile))
            cppType = CaptureStorage(new CppQualifiedType(cursor, CppTypeQualifier.Volatile, cppType), type);
        if (type.IsConstQualified)
        {
            // Skip if it is already qualified.
            if (cppType is CppQualifiedType q && q.qualifier == CppTypeQualifier.Const)
            {
                return cppType;
            }

            return CaptureStorage(new CppQualifiedType(cursor, CppTypeQualifier.Const, cppType), type);
        }
        return cppType;
    }

    private CppType CaptureStorage(
        CppType projection,
        CXType native
    ) {
        long size = native.SizeOf;
        long alignment = native.AlignOf;
        this.rootCompilation.storageFacts[projection] = new(size >= 0 ? size : null, alignment > 0 ? checked((int)alignment) : null,
            native.kind == CXTypeKind.CXType_ConstantArray ? native.ArraySize : null, CXUtil.GetTypeSpelling(native.CanonicalType));
        return projection;
    }

    private CppType GetCppTypeInternal(
        CXCursor cursor,
        CXType type,
        CXCursor parent
    ) {
        if (CppPrimitiveType.kindToPrimitive.TryGetValue(type.kind, out var primitiveType))
        {
            CppPrimitiveType resolved = CppPrimitiveType.ForAbiSize(primitiveType, type.SizeOf);
            bool? signed = type.kind switch
            {
                CXTypeKind.CXType_Char_S or CXTypeKind.CXType_SChar or CXTypeKind.CXType_Short or CXTypeKind.CXType_Int
                    or CXTypeKind.CXType_Long or CXTypeKind.CXType_LongLong or CXTypeKind.CXType_Int128 => true,
                CXTypeKind.CXType_Char_U or CXTypeKind.CXType_UChar or CXTypeKind.CXType_UShort or CXTypeKind.CXType_UInt
                    or CXTypeKind.CXType_ULong or CXTypeKind.CXType_ULongLong or CXTypeKind.CXType_UInt128 => false,
                // These distinct language types have unsigned least-width integer
                // underlying types. Their widths and alignments remain compiler facts.
                CXTypeKind.CXType_Char16 or CXTypeKind.CXType_Char32 => false,
                // libClangSharp's IsSigned/IsUnsigned inspect BitIntType only.
                // CppParser independently evaluates the actual wchar_t sign below.
                CXTypeKind.CXType_WChar => null,
                _ => null
            };
            this.rootCompilation.primitiveFacts[resolved] = new(
                type.AlignOf > 0 ? checked((int)type.AlignOf) : null,
                signed);
            return resolved;
        }

        // The public libclang CXTypeKind enum has no Char8 entry. Require the
        // compiler's builtin type class and exact unqualified canonical spelling;
        // typedefs and similarly named user records retain their own identities.
        if (type.kind == CXTypeKind.CXType_Unexposed && type.CanonicalType.UnqualifiedType.TypeClass == CX_TypeClass.CX_TypeClass_Builtin
            && CXUtil.GetTypeSpelling(type.CanonicalType.UnqualifiedType) == "char8_t")
        {
            CppPrimitiveType resolved = CppPrimitiveType.ForAbiSize(CppPrimitiveType.char8, type.SizeOf);
            this.rootCompilation.primitiveFacts[resolved] = new(type.AlignOf > 0 ? checked((int)type.AlignOf) : null, false);
            return resolved;
        }

        switch (type.kind)
        {
            case CXTypeKind.CXType_ObjCObjectPointer:
            case CXTypeKind.CXType_Pointer:
                return new CppPointerType(cursor, GetCppType(type.PointeeType.Declaration, type.PointeeType, parent), this.rootCompilation.pointerSize);
            case CXTypeKind.CXType_LValueReference:
                return new CppReferenceType(cursor, GetCppType(type.PointeeType.Declaration, type.PointeeType, parent));
            case CXTypeKind.CXType_RValueReference:
                return new CppReferenceType(cursor, GetCppType(type.PointeeType.Declaration, type.PointeeType, parent), isRValue: true);
            case CXTypeKind.CXType_Record:
                return VisitClassDecl(cursor);
            case CXTypeKind.CXType_ObjCInterface:
                return VisitClassDecl(cursor);
            case CXTypeKind.CXType_Enum:
                return VisitReferencedEnum(cursor, parent);
            case CXTypeKind.CXType_FunctionProto:
            case CXTypeKind.CXType_FunctionNoProto:
                return VisitFunctionType(cursor, type, parent);
            case CXTypeKind.CXType_BlockPointer:
                return VisitFunctionType(cursor, type.PointeeType, parent, true);
            case CXTypeKind.CXType_Typedef:
                return (CppType)this.m_context.memberVisitors.GetVisitor<TypedefDeclVisitor>().Visit(this.m_context, cursor, parent)!;
            case CXTypeKind.CXType_Elaborated:
                return VisitElaboratedDecl(cursor, type, parent);
            case CXTypeKind.CXType_ConstantArray:
            case CXTypeKind.CXType_IncompleteArray:
                {
                    var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent);
                    return new CppArrayType(cursor, elementType, GetArrayExtent(type));
                }

            case CXTypeKind.CXType_DependentSizedArray:
                {
                    this.rootCompilation.diagnostics.Warning($"Dependent sized arrays `{CXUtil.GetTypeSpelling(type)}` from `{CXUtil.GetCursorSpelling(parent)}` is not supported", parent.GetSourceLocation());
                    var elementType = GetCppType(type.ArrayElementType.Declaration, type.ArrayElementType, parent);
                    return new CppArrayType(cursor, elementType, 0);
                }

            case CXTypeKind.CXType_Unexposed:
                {
                    // Substitution types in concrete template members may stay unexposed in
                    // libclang while their compiler-provided canonical type is fully concrete.
                    CXType canonical = type.CanonicalType;
                    if (canonical.kind is not (CXTypeKind.CXType_Unexposed or CXTypeKind.CXType_Invalid))
                        return GetCppType(canonical.Declaration, canonical, parent);
                    // It may be possible to parse them even if they are unexposed.
                    var kind = type.Declaration.Type.kind;
                    if (kind != CXTypeKind.CXType_Unexposed && kind != CXTypeKind.CXType_Invalid)
                    {
                        return GetCppType(type.Declaration, type.Declaration.Type, parent);
                    }

                    CppUnexposedType cppUnexposedType = new(cursor, CXUtil.GetTypeSpelling(type))
                    {
                        sizeOf = (int)type.SizeOf
                    };
                    var templateParameters = ParseTemplateSpecializedArguments(cursor, type);
                    if (templateParameters != null)
                    {
                        cppUnexposedType.templateParameters.AddRange(templateParameters);
                    }

                    return cppUnexposedType;
                }

            case CXTypeKind.CXType_Attributed:
                return GetCppType(type.ModifiedType.Declaration, type.ModifiedType, parent);
            case CXTypeKind.CXType_Auto:
                {
                    // A deduced builtin or reference has no declaration cursor.
                    // Its canonical type carries the compiler-established result;
                    // an undeduced auto must stay unknown instead of following an
                    // invalid declaration or guessing from the written spelling.
                    CXType deduced = type.CanonicalType;
                    if (deduced.kind is not (CXTypeKind.CXType_Auto or CXTypeKind.CXType_Invalid))
                        return GetCppType(deduced.Declaration, deduced, parent);
                    return new CppUnexposedType(cursor, CXUtil.GetTypeSpelling(type))
                    {
                        sizeOf = (int)type.SizeOf
                    };
                }
            case CXTypeKind.CXType_ObjCTypeParam:
                {
                    CppTemplateParameterType? templateArgType = this.m_context.TryToCreateTemplateParametersObjC(cursor);
                    if (templateArgType is null)
                    {
                        WarningUnhandled(cursor, parent, type);
                        return new CppUnexposedType(cursor, CXUtil.GetTypeSpelling(type))
                        {
                            sizeOf = (int)type.SizeOf
                        };
                    }

                    // Record that a typedef is using a template parameter type
                    // which will require to re-parent the typedef to the Obj-C interface it belongs to
                    if (this.m_context.currentTypedefKey != default)
                    {
                        var map = this.m_context.mapTemplateParameterTypeToTypedefKeys;
                        if (!map.TryGetValue(templateArgType, out var typedefKeys))
                        {
                            typedefKeys = [];
                            map.Add(templateArgType, typedefKeys);
                        }

                        typedefKeys.Add(this.m_context.currentTypedefKey);
                    }

                    return templateArgType;
                }

            default:
                {
                    WarningUnhandled(cursor, parent, type);
                    return new CppUnexposedType(cursor, CXUtil.GetTypeSpelling(type))
                    {
                        sizeOf = (int)type.SizeOf
                    };
                }
        }
    }

    private static int GetArrayExtent(CXType type)
    {
        long size = type.ArraySize;
        if (size < 0)
            return 0;
        if (size > int.MaxValue)
            throw new NotSupportedException($"Array extent {size} for '{CXUtil.GetTypeSpelling(type)}' exceeds the supported range.");
        return (int)size;
    }

    private struct VisitedFunctionTypeContext
    {
        /// <summary>
        /// Exposes public member <c>Builder</c>.
        /// </summary>
        public CppModelBuilder builder;
        /// <summary>
        /// Exposes public member <c>CppFunction</c>.
        /// </summary>
        public CppFunctionTypeBase cppFunction;
        /// <summary>
        /// Exposes public member <c>IsParsingParameter</c>.
        /// </summary>
        public bool isParsingParameter;
        /// <summary>
        /// Initializes a new instance of <see cref = "VisitedFunctionTypeContext"/>.
        /// </summary>
        public VisitedFunctionTypeContext(
            CppModelBuilder builder,
            CppFunctionTypeBase cppFunction
        ) {
            this.builder = builder;
            this.cppFunction = cppFunction;
        }
    }

    private CppFunctionTypeBase VisitFunctionType(
        CXCursor cursor,
        CXType type,
        CXCursor parent,
        bool isBlockFunctionType = false
    ) {
        // Gets the return type
        var returnType = GetCppType(type.ResultType.Declaration, type.ResultType, cursor);
        var cppFunction = isBlockFunctionType ? (CppFunctionTypeBase)new CppBlockFunctionType(cursor, returnType) : new CppFunctionType(cursor, returnType);
        cppFunction.callingConvention = type.GetCallingConvention();
        cppFunction.isVariadic = type.IsFunctionTypeVariadic;
        cppFunction.hasPrototype = type.kind != CXTypeKind.CXType_FunctionNoProto;
        // We don't use this but use the visitor children to try to recover the parameter names
        //            for (uint i = 0; i < type.NumArgTypes; i++)
        //            {
        //                var argType = type.GetArgType(i);
        //                var cppType = GetCppType(argType.Declaration, argType, type.Declaration, data);
        //                cppFunction.ParameterTypes.Add(cppType);
        //            }
        VisitedFunctionTypeContext ctx = new(this, cppFunction);
        parent.VisitChildren(static (
            argCursor,
            functionCursor,
            clientData
        ) => {
            ref var ctx = ref Unsafe.AsRef<VisitedFunctionTypeContext>(clientData);
            var builder = ctx.builder;
            var cppFunction = ctx.cppFunction;
            if (argCursor.Kind == CXCursorKind.CXCursor_ParmDecl)
            {
                var name = CXUtil.GetCursorSpelling(argCursor);
                var parameterType = builder.GetCppType(argCursor.Type.Declaration, argCursor.Type, argCursor);
                cppFunction.parameters.Add(new CppParameter(argCursor, parameterType, name));
                ctx.isParsingParameter = true;
            }

            return ctx.isParsingParameter ? CXChildVisitResult.CXChildVisit_Continue : CXChildVisitResult.CXChildVisit_Recurse;
        }, (CXClientData)Unsafe.AsPointer(ref ctx));
        // Child cursors recover names; only the function type determines its ABI argument types.
        // A function returning a callback can otherwise borrow the enclosing function's parameters.
        List<CppParameter> recovered = new(cppFunction.parameters);
        cppFunction.parameters.Clear();
        for (int index = 0; index < type.NumArgTypes; index++)
        {
            CXType argument = type.GetArgType((uint)index);
            if (argument.CanonicalType.kind is CXTypeKind.CXType_ConstantArray or CXTypeKind.CXType_IncompleteArray or CXTypeKind.CXType_VariableArray
                or CXTypeKind.CXType_DependentSizedArray or CXTypeKind.CXType_FunctionProto or CXTypeKind.CXType_FunctionNoProto)
                argument = type.CanonicalType.GetArgType((uint)index);
            CppParameter? source = recovered.Count == type.NumArgTypes && recovered[index].cursor.kind != 0
                && recovered[index].cursor.Type.CanonicalType == argument.CanonicalType ? recovered[index] : null;
            CppType parameterType = GetCppType(argument.Declaration, argument, source?.cursor ?? parent);
            if (source is not null)
            {
                source.type = parameterType;
                cppFunction.parameters.Add(source);
            }
            else cppFunction.parameters.Add(new CppParameter(default, parameterType, string.Empty));
        }
        return cppFunction;
    }

    private List<CppType>? ParseTemplateSpecializedArguments(
        CXCursor cursor,
        CXType type
    ) {
        var numTemplateArguments = type.NumTemplateArguments;
        if (numTemplateArguments < 0)
            return null;
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
        CppContainerContext previousRoot = this.m_context.currentRootContainer;
        bool requiredSystemRecord = cursor.Location.IsInSystemHeader && !this.parseSystemIncludes;
        if (cursor.Location.IsInSystemHeader) this.m_context.currentRootContainer = this.m_context.systemRootContainerContext;
        if (requiredSystemRecord) this.m_requiredSystemRecordDepth++;
        try { return VisitClassDeclCore(cursor); }
        finally
        {
            if (requiredSystemRecord) this.m_requiredSystemRecordDepth--;
            this.m_context.currentRootContainer = previousRoot;
        }
    }

    private CppType VisitReferencedEnum(
        CXCursor cursor,
        CXCursor parent
    ) {
        CppContainerContext previousRoot = this.m_context.currentRootContainer;
        bool requiredSystemEnum = cursor.Location.IsInSystemHeader && !this.parseSystemIncludes;
        if (cursor.Location.IsInSystemHeader) this.m_context.currentRootContainer = this.m_context.systemRootContainerContext;
        if (requiredSystemEnum) this.m_requiredSystemEnumDepth++;
        try
        {
            CppType enumeration = (CppType)this.m_context.memberVisitors.GetVisitor<EnumDeclMemberVisitor>().Visit(this.m_context, cursor, parent)!;
            enumeration.AssignSourceSpan(cursor);
            return enumeration;
        }
        finally
        {
            if (requiredSystemEnum) this.m_requiredSystemEnumDepth--;
            this.m_context.currentRootContainer = previousRoot;
        }
    }

    private unsafe CppClass VisitClassDeclCore(CXCursor cursor)
    {
        var cppStruct = this.m_context.GetOrCreateDeclContainer<CppClass>(cursor, out var context);
        if (cursor.Definition.Kind != CXCursorKind.CXCursor_FirstInvalid && cursor != cursor.Definition)
        {
            var definition = VisitClassDecl(cursor.Definition);
            cppStruct.definition = definition;
        }

        bool concreteSpecialization = cppStruct.templateKind == BGCS.Frontend.Clang.Model.Templates.CppTemplateKind.TemplateSpecializedClass && cursor.IsCompleteDefinition;
        if ((cursor.IsCursorDefinition(cppStruct) || concreteSpecialization) && !context.isChildrenVisited)
        {
            ParseAttributes(cursor, cppStruct, false);
            cppStruct.isDefinition = true;
            cppStruct.sizeOf = (int)cursor.Type.SizeOf;
            cppStruct.alignOf = (int)cursor.Type.AlignOf;
            context.isChildrenVisited = true;
            var saveCurrentClassBeingVisited = this.m_context.currentClassBeingVisited;
            this.m_context.currentClassBeingVisited = cppStruct;
            HashSet<CXCursor> visitedMembers = [];
            cursor.VisitChildren((
                child,
                parent,
                data
            ) => {
                visitedMembers.Add(child);
                return VisitMember(child, parent, data);
            }, default);
            if (concreteSpecialization)
            {
                // libclang child traversal omits some instantiated declarations. ClangSharp's
                // declaration-context enumeration exposes the actual substituted members.
                for (uint index = 0; index < cursor.NumDecls; index++)
                {
                    CXCursor member = cursor.GetDecl(index);
                    if (visitedMembers.Add(member)) VisitMember(member, cursor, default);
                }
            }
            if (cppStruct.properties.Count > 0)
            {
                foreach (var prop in cppStruct.properties)
                {
                    prop.getter = cppStruct.functions.FindElementByName(prop.getterName);
                    prop.setter = cppStruct.functions.FindElementByName(prop.setterName);
                }
            }

            cppStruct.AssignSourceSpan(cursor);
            this.m_context.currentClassBeingVisited = saveCurrentClassBeingVisited;
        }

        return cppStruct;
    }

    private CppType VisitElaboratedDecl(
        CXCursor cursor,
        CXType type,
        CXCursor parent
    ) {
        var key = this.m_context.GetCursorKey(cursor);
        if (this.typedefResolver.TryResolve(key, out var typeRef))
        {
            return typeRef;
        }

        // A referenced typedef may originate in an intentionally unvisited SDK
        // header. Materialize that declaration lazily before taking a canonical
        // fallback, preserving alias identity without visiting unrelated SDK AST.
        if (cursor.Kind is CXCursorKind.CXCursor_TypedefDecl or CXCursorKind.CXCursor_TypeAliasDecl)
        {
            CppContainerContext previousRoot = this.m_context.currentRootContainer;
            if (cursor.Location.IsInSystemHeader) this.m_context.currentRootContainer = this.m_context.systemRootContainerContext;
            try
            {
                MemberVisitor visitor = cursor.Kind == CXCursorKind.CXCursor_TypedefDecl
                    ? this.m_context.memberVisitors.GetVisitor<TypedefDeclVisitor>() : this.m_context.memberVisitors.GetVisitor<TypeAliasDeclVisitor>();
                CppType referenced = (CppType)visitor.Visit(this.m_context, cursor, parent)!;
                referenced.AssignSourceSpan(cursor);
                return referenced;
            }
            finally { this.m_context.currentRootContainer = previousRoot; }
        }

        // If the type has been already declared, return it immediately.
        if (this.containers.TryGetValue(key, out var containerContext))
        {
            return (CppType)containerContext.container;
        }

        // TODO: Pseudo fix, we are not supposed to land here, as the TryGet before should resolve an existing type already declared (but not necessarily defined)
        return GetCppType(type.CanonicalType.Declaration, type.CanonicalType, parent);
    }
}
