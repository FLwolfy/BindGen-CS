namespace BGCS
{
    using BGCS.Core.CSharp;
    using BGCS.CppAst.Model;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>Extensions</c>.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Executes public operation <c>FormatLocationAttribute</c>.
        /// </summary>
        public static string FormatLocationAttribute(this CppElement element)
        {
            // TODO: refactor escape string etc.
            var start = element.Span.Start;
            var end = element.Span.End;
            var file = element.SourceFile;
            return $"[SourceLocation(\"{file}\", \"{start}\", \"{end}\")]";
        }

        /// <summary>
        /// Returns computed data from <c>GetCallingConvention</c>.
        /// </summary>
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
        /// Returns computed data from <c>GetCallingConventionDelegate</c>.
        /// </summary>
        public static string GetCallingConventionDelegate(this CppCallingConvention convention)
        {
            return convention switch
            {
                CppCallingConvention.C => "Cdecl",
                CppCallingConvention.X86FastCall => "Fastcall",
                CppCallingConvention.X86StdCall => "Stdcall",
                CppCallingConvention.X86ThisCall => "Thiscall",
                _ => throw new NotSupportedException(),
            };
        }

        /// <summary>
        /// Returns computed data from <c>GetCallingConventionLibrary</c>.
        /// </summary>
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
        /// Returns computed data from <c>GetDirection</c>.
        /// </summary>
        public static Direction GetDirection(this CppType type, bool isPointer = false)
        {
            if (type is CppPrimitiveType)
            {
                return isPointer ? Direction.InOut : Direction.In;
            }

            if (type is CppPointerType pointerType)
            {
                return GetDirection(pointerType.ElementType, true);
            }

            if (type is CppReferenceType)
            {
                return Direction.Out;
            }

            if (type is CppQualifiedType qualifiedType)
            {
                return qualifiedType.Qualifier != CppTypeQualifier.Const && isPointer ? Direction.InOut : Direction.In;
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
        /// Executes public operation <c>CanBeUsedAsOutput</c>.
        /// </summary>
        public static bool CanBeUsedAsOutput(this CppType type, out CppTypeDeclaration? elementTypeDeclaration)
        {
            if (type is CppPointerType pointerType)
            {
                if (pointerType.ElementType is CppTypedef typedef)
                {
                    elementTypeDeclaration = typedef;
                    return true;
                }
                else if (pointerType.ElementType is CppClass @class
                    && @class.ClassKind != CppClassKind.Class
                    && @class.SizeOf > 0)
                {
                    elementTypeDeclaration = @class;
                    return true;
                }
                else if (pointerType.ElementType is CppEnum @enum
                    && @enum.SizeOf > 0)
                {
                    elementTypeDeclaration = @enum;
                    return true;
                }
            }

            elementTypeDeclaration = null;
            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetCanonicalRoot</c>.
        /// </summary>
        public static CppType GetCanonicalRoot(this CppType cppType, bool followTypedefs)
        {
            while (true)
            {
                if (cppType is CppTypeWithElementType elementType)
                {
                    cppType = elementType.ElementType;
                }
                else if (followTypedefs && cppType is CppTypedef typedefType)
                {
                    cppType = typedefType.ElementType;
                }
                else
                {
                    return cppType;
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>TryCast</c>.
        /// </summary>
        public static bool TryCast<TFrom, TTo>(this TFrom from, [NotNullWhen(true), MaybeNullWhen(false)] out TTo? to)
        {
            if (from is TTo casted)
            {
                to = casted;
                return true;
            }
            to = default;
            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsCOMObject</c>.
        /// </summary>
        public static bool IsCOMObject(this CppClass cppClass)
        {
            if (cppClass.Fields.Count == 0 && cppClass.Functions.Count > 0 && cppClass.IsAbstract)
                return true;
            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsClass</c>.
        /// </summary>
        public static bool IsClass(this CppType cppType, [NotNullWhen(true)] out CppClass? cppClass)
        {
            while (true)
            {
                if (cppType is CppPointerType pointerType)
                {
                    cppType = pointerType.ElementType;
                }
                else if (cppType is CppReferenceType referenceType)
                {
                    cppType = referenceType.ElementType;
                }
                else if (cppType is CppQualifiedType qualifiedType)
                {
                    cppType = qualifiedType.ElementType;
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
        /// Executes public operation <c>IsDelegate</c>.
        /// </summary>
        public static bool IsDelegate(this CppPointerType cppPointer, [NotNullWhen(true)] out CppFunctionType? cppFunction)
        {
            if (cppPointer.ElementType is CppFunctionType functionType)
            {
                cppFunction = functionType;
                return true;
            }
            cppFunction = null;
            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsDelegate</c>.
        /// </summary>
        public static bool IsDelegate(this CppType cppType, [NotNullWhen(true)] out CppFunctionType? cppFunction)
        {
            return cppType.GetCanonicalRoot(true).TryCast(out cppFunction);
        }

        /// <summary>
        /// Executes public operation <c>IsDelegate</c>.
        /// </summary>
        public static bool IsDelegate(this CppType cppType) => cppType.IsDelegate(out _);

        /// <summary>
        /// Executes public operation <c>IsDelegate</c>.
        /// </summary>
        public static bool IsDelegate(this CppPointerType cppPointer)
        {
            if (cppPointer.ElementType is CppFunctionType)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsEnum</c>.
        /// </summary>
        public static bool IsEnum(this CppType cppType, [NotNullWhen(true)] out CppEnum? cppEnum)
        {
            while (true)
            {
                if (cppType is CppQualifiedType qualifiedType)
                {
                    cppType = qualifiedType.ElementType;
                }
                else if (cppType is CppTypedef cppTypedef)
                {
                    cppType = cppTypedef.ElementType;
                }
                else if (cppType is CppPointerType cppPointer)
                {
                    cppType = cppPointer.ElementType;
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
        /// Executes public operation <c>IsEnum</c>.
        /// </summary>
        public static bool IsEnum(this CppType cppType)
        {
            return cppType.IsEnum(out _);
        }

        /// <summary>
        /// Executes public operation <c>IsOpaqueHandle</c>.
        /// </summary>
        public static bool IsOpaqueHandle(this CppTypedef typedef)
        {
            if (typedef.ElementType is CppPointerType pointerType && pointerType.ElementType is CppClass classType)
            {
                return !classType.IsDefinition;
            }
            return false;
        }

        /// <summary>
        /// Executes public operation <c>ToCamelCase</c>.
        /// </summary>
        public static unsafe string ToCamelCase(this string str)
        {
            bool wasNumber = false;
            string output = new('\0', str.Length);
            fixed (char* p = output)
            {
                p[0] = char.ToUpper(str[0]);
                for (int i = 1; i < str.Length; i++)
                {
                    if (wasNumber)
                    {
                        p[i] = char.ToUpper(str[i]);
                    }
                    else
                    {
                        p[i] = char.ToLower(str[i]);
                    }
                    wasNumber = char.IsDigit(str[i]);
                }
            }
            return output;
        }

        private enum SplitByCaseModes
        { None, WhiteSpace, Digit, UpperCase, LowerCase }

        /// <summary>
        /// Executes public operation <c>SplitByCase</c>.
        /// </summary>
        public static string[] SplitByCase(this string s)
        {
            var ʀ = new List<string>();
            var ᴛ = new StringBuilder();
            var previous = SplitByCaseModes.None;
            foreach (var ɪ in s)
            {
                SplitByCaseModes mode_ɪ;
                if (string.IsNullOrWhiteSpace(ɪ.ToString()))
                {
                    mode_ɪ = SplitByCaseModes.WhiteSpace;
                }
                else if (char.IsDigit(ɪ))
                {
                    mode_ɪ = SplitByCaseModes.Digit;
                }
                else if (ɪ == ɪ.ToString().ToUpper()[0])
                {
                    mode_ɪ = SplitByCaseModes.UpperCase;
                }
                else
                {
                    mode_ɪ = SplitByCaseModes.LowerCase;
                }
                if (previous == SplitByCaseModes.None || previous == mode_ɪ)
                {
                    ᴛ.Append(ɪ);
                }
                else if (previous == SplitByCaseModes.UpperCase && mode_ɪ == SplitByCaseModes.LowerCase)
                {
                    if (ᴛ.Length > 1)
                    {
                        ʀ.Add(ᴛ.ToString()[..(ᴛ.Length - 1)]);
                        ᴛ.Remove(0, ᴛ.Length - 1);
                    }
                    ᴛ.Append(ɪ);
                }
                else
                {
                    ʀ.Add(ᴛ.ToString());
                    ᴛ.Clear();
                    ᴛ.Append(ɪ);
                }
                previous = mode_ɪ;
            }
            if (ᴛ.Length != 0) ʀ.Add(ᴛ.ToString());
            return ʀ.ToArray();
        }

        /// <summary>
        /// Executes public operation <c>FindMacro</c>.
        /// </summary>
        public static CppMacro? FindMacro(this CppCompilation compilation, string name)
        {
            for (int i = 0; i < compilation.Macros.Count; i++)
            {
                var macro = compilation.Macros[i];
                if (macro.Name == name)
                    return macro;
            }
            return null;
        }

        /// <summary>
        /// Executes public operation <c>TryFindMacro</c>.
        /// </summary>
        public static bool TryFindMacro(this CppCompilation compilation, string name, [NotNullWhen(true)] out CppMacro? macro)
        {
            macro = FindMacro(compilation, name);
            return macro != null;
        }
    }
}
