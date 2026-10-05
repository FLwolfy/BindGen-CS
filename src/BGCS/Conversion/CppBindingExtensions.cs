using System;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.Conversion
{
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.InteropServices;
    using BGCS.CppAst.Model;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;
    using BGCS.CSharp;

    /// <summary>
    /// Classifies native type shapes and projects supported calling conventions during managed binding analysis.
    /// </summary>
    public static class CppBindingExtensions
    {
        /// <summary>
        /// Builds a source-location attribute fragment with escaped C# string literals.
        /// </summary>
        /// <param name="element">
        /// The modeled declaration whose source file and span will be included.
        /// </param>
        /// <returns>
        /// An attribute fragment with escaped file, start, and end strings; this does not validate the attribute type exists.
        /// </returns>
        public static string FormatLocationAttribute(this CppElement element)
        {
            var start = element.span.start;
            var end = element.span.end;
            var file = element.sourceFile;
            return $"[SourceLocation({SyntaxFactory.Literal(file ?? string.Empty).Text}, {SyntaxFactory.Literal(start.ToString()).Text}, {SyntaxFactory.Literal(end.ToString()).Text})]";
        }

        /// <summary>
        /// Maps a supported native calling convention to its managed interop representation.
        /// </summary>
        /// <param name="convention">
        /// The native declaration calling convention to translate.
        /// </param>
        /// <returns>
        /// The corresponding runtime interop calling-convention value.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// This projection does not support the supplied calling convention.
        /// </exception>
        public static CallingConvention GetCallingConvention(this CppCallingConvention convention)
        {
            return convention switch
            {
                CppCallingConvention.C => CallingConvention.Cdecl,
                CppCallingConvention.Win64 => CallingConvention.Winapi,
                CppCallingConvention.X86FastCall => CallingConvention.FastCall,
                CppCallingConvention.X86StdCall => CallingConvention.StdCall,
                CppCallingConvention.X86ThisCall => CallingConvention.ThisCall,
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Maps a supported native calling convention to its managed interop representation.
        /// </summary>
        /// <param name="convention">
        /// The native declaration calling convention to translate.
        /// </param>
        /// <returns>
        /// The convention identifier used inside a C# unmanaged function-pointer type.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// This projection does not support the supplied calling convention.
        /// </exception>
        public static string GetCallingConventionDelegate(this CppCallingConvention convention)
        {
            return convention switch
            {
                CppCallingConvention.Default or CppCallingConvention.C or CppCallingConvention.Win64 or CppCallingConvention.X86_64SysV or CppCallingConvention.AAPCS or CppCallingConvention.AAPCS_VFP => "Cdecl",
                CppCallingConvention.X86FastCall => "Fastcall",
                CppCallingConvention.X86StdCall => "Stdcall",
                CppCallingConvention.X86ThisCall => "Thiscall",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Maps a supported native calling convention to its managed interop representation.
        /// </summary>
        /// <param name="convention">
        /// The native declaration calling convention to translate.
        /// </param>
        /// <returns>
        /// The fully qualified call-convention marker type for an unmanaged import.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// This projection does not support the supplied calling convention.
        /// </exception>
        public static string GetCallingConventionLibrary(this CppCallingConvention convention)
        {
            return convention switch
            {
                CppCallingConvention.C => "System.Runtime.CompilerServices.CallConvCdecl",
                CppCallingConvention.X86FastCall => "System.Runtime.CompilerServices.CallConvFastcall",
                CppCallingConvention.X86StdCall => "System.Runtime.CompilerServices.CallConvStdcall",
                CppCallingConvention.X86ThisCall => "System.Runtime.CompilerServices.CallConvThiscall",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Infers a parameter direction from native pointer, reference, and const qualification shapes.
        /// </summary>
        /// <param name="type">
        /// The native type shape to inspect.
        /// </param>
        /// <param name="isPointer">
        /// Whether an enclosing pointer was already encountered during traversal.
        /// </param>
        /// <returns>
        /// In for values or const-qualified pointer targets, Out for references, and InOut for mutable pointer targets; this is a shape heuristic.
        /// </returns>
        public static Direction GetDirection(
            this CppType type,
            bool isPointer = false
        ) {
            if (type is CppPrimitiveType)
            {
                return isPointer ? Direction.InOut : Direction.In;
            }

            if (type is CppPointerType pointerType)
            {
                return GetDirection(pointerType.elementType, true);
            }

            if (type is CppReferenceType)
            {
                return Direction.Out;
            }

            if (type is CppQualifiedType qualifiedType)
            {
                return qualifiedType.qualifier != CppTypeQualifier.Const && isPointer ? Direction.InOut : Direction.In;
            }

            if (type is CppFunctionType)
            {
                return isPointer ? Direction.InOut : Direction.In;
            }

            if (type is CppTypedef)
            {
                return isPointer ? Direction.InOut : Direction.In;
            }

            if (type is CppClass)
            {
                return isPointer ? Direction.InOut : Direction.In;
            }

            if (type is CppEnum)
            {
                return isPointer ? Direction.InOut : Direction.In;
            }

            return isPointer ? Direction.InOut : Direction.In;
        }

        /// <summary>
        /// Checks whether a direct pointer targets a typedef or a sized enum or struct declaration suitable for an out projection.
        /// </summary>
        /// <param name="type">
        /// The native parameter type to inspect.
        /// </param>
        /// <param name="elementTypeDeclaration">
        /// Receives the directly pointed-to declaration on success, or null on failure.
        /// </param>
        /// <returns>
        /// True for supported direct pointer targets; false for other shapes or unsized concrete declarations.
        /// </returns>
        public static bool CanBeUsedAsOutput(
            this CppType type,
            out CppTypeDeclaration? elementTypeDeclaration
        ) {
            if (type is CppPointerType pointerType)
            {
                if (pointerType.elementType is CppTypedef typedef)
                {
                    elementTypeDeclaration = typedef;
                    return true;
                }
                else if (pointerType.elementType is CppClass @class && @class.classKind != CppClassKind.Class && @class.sizeOf > 0)
                {
                    elementTypeDeclaration = @class;
                    return true;
                }
                else if (pointerType.elementType is CppEnum @enum && @enum.sizeOf > 0)
                {
                    elementTypeDeclaration = @enum;
                    return true;
                }
            }

            elementTypeDeclaration = null;
            return false;
        }

        /// <summary>
        /// Unwraps element-bearing types and optionally typedef chains to the final modeled root.
        /// </summary>
        /// <param name="cppType">
        /// The native type to traverse.
        /// </param>
        /// <param name="followTypedefs">
        /// Whether typedef declarations are traversed instead of retained as the root.
        /// </param>
        /// <returns>
        /// The borrowed root descriptor; no parser objects are copied or transferred.
        /// </returns>
        public static CppType GetCanonicalRoot(
            this CppType cppType,
            bool followTypedefs
        ) {
            while (true)
            {
                if (cppType is CppTypeWithElementType elementType)
                {
                    cppType = elementType.elementType;
                }
                else if (followTypedefs && cppType is CppTypedef typedefType)
                {
                    cppType = typedefType.elementType;
                }
                else
                {
                    return cppType;
                }
            }
        }

        /// <summary>
        /// Checks the current runtime type without performing a conversion.
        /// </summary>
        /// <typeparam name="TFrom">
        /// The source value type.
        /// </typeparam>
        /// <typeparam name="TTo">
        /// The requested runtime type.
        /// </typeparam>
        /// <param name="from">
        /// The value to inspect.
        /// </param>
        /// <param name="to">
        /// Receives the typed value on success, or the target default on failure.
        /// </param>
        /// <returns>
        /// True when the value already has the requested target type.
        /// </returns>
        public static bool TryCast<TFrom, TTo>(
            this TFrom from,
            [NotNullWhen(true), MaybeNullWhen(false)] out TTo? to
        ) {
            if (from is TTo casted)
            {
                to = casted;
                return true;
            }

            to = default;
            return false;
        }

        /// <summary>
        /// Applies the method-only abstract-class heuristic used for COM-like wrapper analysis.
        /// </summary>
        /// <param name="cppClass">
        /// The native class descriptor to inspect.
        /// </param>
        /// <returns>
        /// True when the class is abstract, has methods, and has no fields; this does not validate COM ABI or identity semantics.
        /// </returns>
        public static bool IsCOMObject(this CppClass cppClass)
        {
            if (cppClass.fields.Count == 0 && cppClass.functions.Count > 0 && cppClass.isAbstract)
                return true;
            return false;
        }

        /// <summary>
        /// Unwraps pointers, references, and qualification to find a class declaration.
        /// </summary>
        /// <param name="cppType">
        /// The native type shape to traverse; typedefs are not traversed by this predicate.
        /// </param>
        /// <param name="cppClass">
        /// Receives the borrowed class descriptor, or null when no class shape is reached.
        /// </param>
        /// <returns>
        /// True when the unwrapped shape is a modeled class.
        /// </returns>
        public static bool IsClass(
            this CppType cppType,
            [NotNullWhen(true)] out CppClass? cppClass
        ) {
            while (true)
            {
                if (cppType is CppPointerType pointerType)
                {
                    cppType = pointerType.elementType;
                }
                else if (cppType is CppReferenceType referenceType)
                {
                    cppType = referenceType.elementType;
                }
                else if (cppType is CppQualifiedType qualifiedType)
                {
                    cppType = qualifiedType.elementType;
                }
                else if (cppType is CppClass cpp)
                {
                    cppClass = cpp;
                    return true;
                }
                else
                {
                    cppClass = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks for a native function type through the supported wrapper shapes.
        /// </summary>
        /// <param name="cppPointer">
        /// The pointer whose direct target is inspected.
        /// </param>
        /// <param name="cppFunction">
        /// Receives the borrowed function-signature descriptor, or null on failure.
        /// </param>
        /// <returns>
        /// True when a native function signature is reached.
        /// </returns>
        public static bool IsDelegate(
            this CppPointerType cppPointer,
            [NotNullWhen(true)] out CppFunctionType? cppFunction
        ) {
            if (cppPointer.elementType is CppFunctionType functionType)
            {
                cppFunction = functionType;
                return true;
            }

            cppFunction = null;
            return false;
        }

        /// <summary>
        /// Checks for a native function type through the supported wrapper shapes.
        /// </summary>
        /// <param name="cppType">
        /// The native type whose element and typedef chains are traversed.
        /// </param>
        /// <param name="cppFunction">
        /// Receives the borrowed function-signature descriptor, or null on failure.
        /// </param>
        /// <returns>
        /// True when a native function signature is reached.
        /// </returns>
        public static bool IsDelegate(
            this CppType cppType,
            [NotNullWhen(true)] out CppFunctionType? cppFunction
        ) {
            return cppType.GetCanonicalRoot(true).TryCast(out cppFunction);
        }

        /// <summary>
        /// Checks whether the native type shape resolves to a function signature.
        /// </summary>
        /// <param name="cppType">
        /// The native type to inspect through its supported pointer or canonical-root projection.
        /// </param>
        /// <returns>
        /// True when a native function signature is reached.
        /// </returns>
        public static bool IsDelegate(this CppType cppType) => cppType.IsDelegate(out _);
        /// <summary>
        /// Checks whether the native type shape resolves to a function signature.
        /// </summary>
        /// <param name="cppPointer">
        /// The native type to inspect through its supported pointer or canonical-root projection.
        /// </param>
        /// <returns>
        /// True when a native function signature is reached.
        /// </returns>
        public static bool IsDelegate(this CppPointerType cppPointer)
        {
            if (cppPointer.elementType is CppFunctionType)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Traverses qualification, typedefs, and pointers to find a native enum.
        /// </summary>
        /// <param name="cppType">
        /// The native type shape to inspect; reference wrappers are not traversed.
        /// </param>
        /// <param name="cppEnum">
        /// Receives the borrowed enum declaration, or null when no enum is reached.
        /// </param>
        /// <returns>
        /// True when the supported wrappers resolve to an enum.
        /// </returns>
        public static bool IsEnum(
            this CppType cppType,
            [NotNullWhen(true)] out CppEnum? cppEnum
        ) {
            while (true)
            {
                if (cppType is CppQualifiedType qualifiedType)
                {
                    cppType = qualifiedType.elementType;
                }
                else if (cppType is CppTypedef cppTypedef)
                {
                    cppType = cppTypedef.elementType;
                }
                else if (cppType is CppPointerType cppPointer)
                {
                    cppType = cppPointer.elementType;
                }
                else if (cppType is CppEnum cppEnumType)
                {
                    cppEnum = cppEnumType;
                    return true;
                }
                else
                {
                    cppEnum = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks whether qualification, typedef, and pointer traversal reaches a native enum.
        /// </summary>
        /// <param name="cppType">
        /// The native type shape to inspect.
        /// </param>
        /// <returns>
        /// True when the supported wrappers resolve to an enum.
        /// </returns>
        public static bool IsEnum(this CppType cppType)
        {
            return cppType.IsEnum(out _);
        }

        /// <summary>
        /// Checks whether a typedef directly aliases a pointer to an incomplete native class.
        /// </summary>
        /// <param name="typedef">
        /// The native alias declaration to inspect.
        /// </param>
        /// <returns>
        /// True for a direct pointer to a class without a definition; false for all other alias shapes.
        /// </returns>
        public static bool IsOpaqueHandle(this CppTypedef typedef)
        {
            if (typedef.elementType is CppPointerType pointerType && pointerType.elementType is CppClass classType)
            {
                return !classType.isDefinition;
            }

            return false;
        }

    }
}
