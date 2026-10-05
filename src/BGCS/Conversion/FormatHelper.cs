using System.Collections.Generic;
using System.Linq;
using BGCS.Configuration;
using BGCS.CppAst.Extensions;

namespace BGCS.Conversion
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// Provides native type-shape queries and supported source-spelling projections used during binding analysis.
    /// </summary>
    public static class FormatHelper
    {
        /// <summary>
        /// Tests whether a spelling contains no lowercase characters; this is a naming heuristic rather than a letter-only check.
        /// </summary>
        /// <param name="str">
        /// The spelling to inspect.
        /// </param>
        /// <returns>
        /// True when no lowercase character occurs, including an empty spelling; otherwise false.
        /// </returns>
        public static bool IsCaps(this string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                var c = str[i];
                if (char.IsSymbol(c))
                    continue;
                if (char.IsLower(c))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks for a direct pointer after removing any outer qualification wrappers.
        /// </summary>
        /// <param name="type">
        /// The borrowed parsed type.
        /// </param>
        /// <returns>
        /// True for a pointer or qualified pointer; typedef aliases are not followed by this overload.
        /// </returns>
        public static bool IsPointer(this CppType type)
        {
            if (type is CppPointerType)
            {
                return true;
            }

            if (type is CppQualifiedType qualifiedType)
            {
                return IsPointer(qualifiedType.elementType);
            }

            return false;
        }

        /// <summary>
        /// Counts consecutive direct pointer wrappers without following typedef or qualification wrappers.
        /// </summary>
        /// <param name="type">
        /// The borrowed parsed type.
        /// </param>
        /// <param name="depth">
        /// Replaced by the number of directly nested pointer wrappers.
        /// </param>
        /// <returns>
        /// True when at least one direct pointer wrapper was removed; otherwise false.
        /// </returns>
        public static bool IsPointer(
            this CppType type,
            ref int depth
        ) {
            bool isPointer = false;
            CppType d = type;
            depth = 0;
            while (true)
            {
                if (d is CppPointerType pointer)
                {
                    depth++;
                    d = pointer.elementType;
                    isPointer = true;
                }
                else
                {
                    break;
                }
            }

            return isPointer;
        }

        /// <summary>
        /// Counts consecutive direct pointer wrappers without following typedef or qualification wrappers.
        /// </summary>
        /// <param name="type">
        /// The borrowed parsed type.
        /// </param>
        /// <param name="depth">
        /// Replaced by the number of directly nested pointer wrappers.
        /// </param>
        /// <param name="pointerType">
        /// The retained innermost type after removing direct pointer wrappers, or the original type when no pointer exists.
        /// </param>
        /// <returns>
        /// True when at least one direct pointer wrapper was removed; otherwise false.
        /// </returns>
        public static bool IsPointer(
            this CppType type,
            ref int depth,
            out CppType pointerType
        ) {
            bool isPointer = false;
            CppType d = type;
            depth = 0;
            while (true)
            {
                if (d is CppPointerType pointer)
                {
                    depth++;
                    d = pointer.elementType;
                    isPointer = true;
                }
                else
                {
                    break;
                }
            }

            pointerType = d;
            return isPointer;
        }

        /// <summary>
        /// Checks whether a pointer resolves to the requested type spelling after alias and qualification removal.
        /// </summary>
        /// <param name="type">
        /// The requested borrowed pointee type.
        /// </param>
        /// <param name="pointer">
        /// The borrowed candidate pointer type.
        /// </param>
        /// <returns>
        /// True for one matching pointer level; otherwise false.
        /// </returns>
        public static bool IsPointerOf(
            this CppType type,
            CppType pointer
        ) {
            type = UnwrapAliases(type);
            pointer = UnwrapAliases(pointer);
            if (pointer is CppPointerType pointerType)
            {
                return UnwrapAliases(pointerType.elementType).GetDisplayName() == type.GetDisplayName();
            }

            return false;
        }

        /// <summary>
        /// Follows pointer levels after alias and qualification removal and compares the innermost pointee spelling.
        /// </summary>
        /// <param name="type">
        /// The requested borrowed pointee type.
        /// </param>
        /// <param name="pointer">
        /// The borrowed candidate pointer type.
        /// </param>
        /// <param name="depth">
        /// Incremented for each pointer level; reset to zero when the candidate is not a pointer.
        /// </param>
        /// <returns>
        /// True when the innermost pointee spelling matches; otherwise false.
        /// </returns>
        public static bool IsPointerOf(
            this CppType type,
            CppType pointer,
            ref int depth
        ) {
            type = UnwrapAliases(type);
            pointer = UnwrapAliases(pointer);
            if (pointer is CppPointerType pointerType)
            {
                CppType elementType = UnwrapAliases(pointerType.elementType);
                if (elementType is CppPointerType cppPointer)
                {
                    depth++;
                    return IsPointerOf(type, cppPointer, ref depth);
                }

                depth++;
                return elementType.GetDisplayName() == type.GetDisplayName();
            }

            depth = 0;
            return false;
        }

        private static CppType UnwrapAliases(CppType type)
        {
            while (true)
            {
                switch (type)
                {
                    case CppTypedef typedef:
                        type = typedef.elementType;
                        continue;
                    case CppQualifiedType qualified:
                        type = qualified.elementType;
                        continue;
                    default:
                        return type;
                }
            }
        }

        /// <summary>
        /// Compares parsed types by their displayed native type spelling.
        /// </summary>
        /// <param name="a">
        /// The first borrowed parsed type.
        /// </param>
        /// <param name="b">
        /// The second borrowed parsed type.
        /// </param>
        /// <returns>
        /// True when both displayed spellings are identical; otherwise false. This comparison does not validate layout identity.
        /// </returns>
        public static bool IsType(
            this CppType a,
            CppType b
        ) {
            return a.GetDisplayName() == b.GetDisplayName();
        }

        /// <summary>
        /// Follows typedef and pointer wrappers to locate a primitive type; qualification and other wrappers do not match.
        /// </summary>
        /// <param name="cppType">
        /// The borrowed parsed type.
        /// </param>
        /// <param name="primitive">
        /// The retained primitive type on success, or null when no supported primitive path exists.
        /// </param>
        /// <returns>
        /// True when a primitive is found through supported wrappers; otherwise false.
        /// </returns>
        public static bool IsPrimitive(
            this CppType cppType,
            [NotNullWhen(true)] out CppPrimitiveType? primitive
        ) {
            if (cppType is CppPrimitiveType cppPrimitive)
            {
                primitive = cppPrimitive;
                return true;
            }

            if (cppType is CppTypedef cppTypedef)
            {
                return IsPrimitive(cppTypedef.elementType, out primitive);
            }

            if (cppType is CppPointerType cppPointerType)
            {
                return IsPrimitive(cppPointerType.elementType, out primitive);
            }

            primitive = null;
            return false;
        }

        /// <summary>
        /// Determines whether a typedef or qualified type ultimately aliases a native pointer.
        /// </summary>
        /// <param name = "cppType">Type to inspect.</param>
        /// <returns><see langword="true"/> when the alias chain ends in a pointer type.</returns>
        public static bool IsPointerAlias(this CppType cppType)
        {
            while (cppType is CppTypedef typedef)
                cppType = typedef.elementType;
            while (cppType is CppQualifiedType qualified)
                cppType = qualified.elementType;
            return cppType is CppPointerType;
        }

        /// <summary>
        /// Scans top-level functions and record fields for pointers whose innermost pointee matches the requested record spelling.
        /// </summary>
        /// <param name="cppClass">
        /// The borrowed parsed record to query.
        /// </param>
        /// <param name="compilation">
        /// The caller-owned compilation containing the scanned declarations.
        /// </param>
        /// <param name="depths">
        /// A new list containing each distinct matching pointer depth once.
        /// </param>
        /// <returns>
        /// True when at least one matching pointer use exists in the scanned declarations; otherwise false.
        /// </returns>
        public static bool IsUsedAsPointer(
            this CppClass cppClass,
            CppCompilation compilation,
            out List<int> depths
        ) {
            depths = new List<int>();
            int depth = 0;
            for (int i = 0; i < compilation.functions.Count; i++)
            {
                depth = 0;
                var func = compilation.functions[i];
                if (IsPointerOf(cppClass, func.returnType, ref depth))
                {
                    if (!depths.Contains(depth))
                        depths.Add(depth);
                }

                for (int j = 0; j < func.parameters.Count; j++)
                {
                    depth = 0;
                    var param = func.parameters[j];
                    if (IsPointerOf(cppClass, param.type, ref depth))
                    {
                        if (!depths.Contains(depth))
                            depths.Add(depth);
                    }
                }
            }

            for (int i = 0; i < compilation.classes.Count; i++)
            {
                var cl = compilation.classes[i];
                for (int j = 0; j < cl.fields.Count; j++)
                {
                    depth = 0;
                    var field = cl.fields[j];
                    if (IsPointerOf(cppClass, field.type, ref depth))
                    {
                        if (!depths.Contains(depth))
                            depths.Add(depth);
                    }
                }
            }

            return depths.Count > 0;
        }

        /// <summary>
        /// Rewrites supported unsigned all-bits expressions and ULL suffix spellings into managed enum expression spelling.
        /// </summary>
        /// <param name="value">
        /// The original native enum expression spelling.
        /// </param>
        /// <returns>
        /// The projected spelling; unsupported expression forms remain unchanged.
        /// </returns>
        public static string NormalizeEnumValue(this string value)
        {
            if (value == "(~0U)")
            {
                return "~0u";
            }

            if (value == "(~0ULL)")
            {
                return "~0ul";
            }

            if (value == "(~0U-1)")
            {
                return "~0u - 1";
            }

            if (value == "(~0U-2)")
            {
                return "~0u - 2";
            }

            if (value == "(~0U-3)")
            {
                return "~0u - 3";
            }

            return value.Replace("ULL", "UL");
        }

        /// <summary>
        /// Projects supported unsigned numeric spellings and native string-literal forms, combining adjacent quoted string fragments.
        /// </summary>
        /// <param name="value">
        /// The original native constant spelling.
        /// </param>
        /// <returns>
        /// The projected managed spelling; unrecognized forms retain their original text except the supported ULL suffix rewrite.
        /// </returns>
        public static string NormalizeConstantValue(this string value)
        {
            if (value == "(~0U)")
            {
                return "~0u";
            }

            if (value == "(~0ULL)")
            {
                return "~0ul";
            }

            if (value == "(~0U-1)")
            {
                return "~0u - 1";
            }

            if (value == "(~0U-2)")
            {
                return "~0u - 2";
            }

            if (value == "(~0U-3)")
            {
                return "~0u - 3";
            }

            if (TryCombineAdjacentCStringLiterals(value, out string? combinedString))
            {
                return combinedString;
            }

            if ((value.StartsWith("L\"") || value.StartsWith("R\"") || value.StartsWith("LR\"")) && value.EndsWith("\"") && value.Count(c => c == '"') > 2)
            {
                string[] parts = value.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                StringBuilder sb = new();
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (part == "L" || part == "R" || part == "LR")
                        continue;
                    sb.Append(part);
                }

                return $"@\"{sb}\"";
            }
            else
            {
                if (value.StartsWith("L\"") && value.EndsWith("\""))
                {
                    return value[1..];
                }

                if (value.StartsWith("R\"") && value.EndsWith("\""))
                {
                    return $"@{value[1..]}";
                }

                if (value.StartsWith("LR\"") && value.EndsWith("\""))
                {
                    var lines = value[3..^1].Split("\n");
                    for (int i = 0; i < lines.Length; i++)
                    {
                        lines[i] = lines[i].TrimEnd('\r');
                    }

                    return $"@\"{string.Join("\n", lines)}\"";
                }
            }

            return value.Replace("ULL", "UL");
        }

        private static bool TryCombineAdjacentCStringLiterals(
            string value,
            [NotNullWhen(true)] out string? result
        ) {
            StringBuilder combined = new();
            int index = 0;
            int literals = 0;
            while (true)
            {
                while (index < value.Length && char.IsWhiteSpace(value[index]))
                    index++;
                if (index >= value.Length)
                    break;
                if (value.AsSpan(index).StartsWith("u8\"", StringComparison.Ordinal))
                    index += 2;
                else if (value[index] is 'L' or 'u' or 'U')
                    index++;
                if (index >= value.Length || value[index] != '"')
                {
                    result = null;
                    return false;
                }

                index++;
                bool closed = false;
                while (index < value.Length)
                {
                    char current = value[index++];
                    if (current == '\\' && index < value.Length)
                    {
                        combined.Append(current).Append(value[index++]);
                        continue;
                    }

                    if (current == '"')
                    {
                        closed = true;
                        break;
                    }

                    combined.Append(current);
                }

                if (!closed)
                {
                    result = null;
                    return false;
                }

                literals++;
            }

            if (literals < 2)
            {
                result = null;
                return false;
            }

            result = $"\"{combined}\"";
            return true;
        }

        /// <summary>
        /// Tests an expression against the limited character alphabet used by the numeric-expression projection heuristic.
        /// </summary>
        /// <param name="expression">
        /// The candidate spelling; no parsing or constant evaluation occurs.
        /// </param>
        /// <returns>
        /// True when all characters are letters, numbers, a decimal point, or a supported arithmetic or shift character; an empty spelling also satisfies this heuristic.
        /// </returns>
        public static bool IsConstantExpression(this string expression)
        {
            for (int i = 0; i < expression.Length; i++)
            {
                var c = expression[i];
                if (char.IsLetter(c))
                {
                    continue;
                }

                if (char.IsNumber(c) || c == '.')
                {
                    continue;
                }

                if (c == '+' || c == '-' || c == '*' || c == '/' || c == '<' || c == '>')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Tests whether a spelling starts and ends with double-quote delimiters without decoding its contents.
        /// </summary>
        /// <param name="name">
        /// The candidate source spelling.
        /// </param>
        /// <returns>
        /// True when both quote delimiters exist; this heuristic does not validate escapes or source-language syntax.
        /// </returns>
        public static bool IsString(this string name)
        {
            return name.StartsWith("\"") && name.EndsWith("\"");
        }

        /// <summary>
        /// Checks whether the immediate parsed type is the primitive no-value return type.
        /// </summary>
        /// <param name="cppType">
        /// The borrowed parsed type; wrappers are not followed.
        /// </param>
        /// <returns>
        /// True for the direct primitive void type; otherwise false.
        /// </returns>
        public static bool IsVoid(this CppType cppType)
        {
            if (cppType is CppPrimitiveType type)
            {
                return type.kind == CppPrimitiveKind.Void;
            }

            return false;
        }

        /// <summary>
        /// Checks character-pointer shape through supported aliases and qualifications, honoring explicit pointer carrier mappings.
        /// </summary>
        /// <param name="cppType">
        /// The borrowed parsed candidate type.
        /// </param>
        /// <param name="config">
        /// The mapping configuration used when resolving aliases.
        /// </param>
        /// <param name="stringKind">
        /// The recognized character primitive on success, or Void when no character-pointer shape matches.
        /// </param>
        /// <param name="isPointer">
        /// Whether an outer pointer was already traversed by the caller.
        /// </param>
        /// <returns>
        /// True for a supported character-pointer shape; otherwise false.
        /// </returns>
        public static bool IsString(
            this CppType cppType,
            CsCodeGeneratorConfig config,
            out CppPrimitiveKind stringKind,
            bool isPointer = false
        ) {
            if (cppType is CppPointerType pointer && !isPointer)
            {
                return IsString(pointer.elementType, config, out stringKind, true);
            }

            if (cppType is CppQualifiedType qualified)
            {
                return IsString(qualified.elementType, config, out stringKind, isPointer);
            }

            if (cppType is CppTypedef typedef)
            {
                if (config.typeMappings.TryGetValue(typedef.name, out var type))
                {
                    if (!isPointer && type == "char*")
                    {
                        stringKind = CppPrimitiveKind.WChar;
                        return true;
                    }

                    if (!isPointer && type == "byte*")
                    {
                        stringKind = CppPrimitiveKind.Char;
                        return true;
                    }

                    stringKind = CppPrimitiveKind.Void;
                    return false;
                }

                return IsString(typedef.elementType, config, out stringKind, isPointer);
            }

            if (isPointer && cppType is CppPrimitiveType primitive)
            {
                stringKind = primitive.kind;
                return primitive.kind == CppPrimitiveKind.WChar || primitive.kind == CppPrimitiveKind.Char;
            }

            stringKind = CppPrimitiveKind.Void;
            return false;
        }

        /// <summary>
        /// Follows pointer, reference, array, and qualification wrappers and checks retained template parameter identity.
        /// </summary>
        /// <param name="type">
        /// The borrowed parsed type to inspect.
        /// </param>
        /// <param name="function">
        /// The parsed function whose template parameters are compared.
        /// </param>
        /// <returns>
        /// True when the unwrapped type is one of the function's retained template parameter objects; otherwise false.
        /// </returns>
        public static bool IsTemplateParameter(
            this CppType type,
            CppFunction function
        ) {
            if (type is CppPointerType pointer)
            {
                return IsTemplateParameter(pointer.elementType, function);
            }

            if (type is CppReferenceType reference)
            {
                return IsTemplateParameter(reference.elementType, function);
            }

            if (type is CppArrayType array)
            {
                return IsTemplateParameter(array.elementType, function);
            }

            if (type is CppQualifiedType qualified)
            {
                return IsTemplateParameter(qualified.elementType, function);
            }

            if (type is CppUnexposedType unexposed)
            {
                for (int i = 0; i < function.templateParameters.Count; i++)
                {
                    if (function.templateParameters[i] == unexposed)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Projects a template parameter placeholder while preserving pointer-like wrappers as managed pointer suffixes.
        /// </summary>
        /// <param name="type">
        /// The borrowed parsed type shape.
        /// </param>
        /// <param name="genericName">
        /// The managed generic parameter spelling used for an unexposed placeholder.
        /// </param>
        /// <returns>
        /// The projected spelling, or an empty string when no supported template placeholder is found.
        /// </returns>
        public static string GetTemplateParameterCsName(
            this CppType type,
            string genericName
        ) {
            if (type is CppPointerType pointer)
            {
                return GetTemplateParameterCsName(pointer.elementType, genericName) + "*";
            }

            if (type is CppReferenceType reference)
            {
                return GetTemplateParameterCsName(reference.elementType, genericName) + "*";
            }

            if (type is CppArrayType array)
            {
                return GetTemplateParameterCsName(array.elementType, genericName) + "*";
            }

            if (type is CppQualifiedType qualified)
            {
                return GetTemplateParameterCsName(qualified.elementType, genericName);
            }

            if (type is CppUnexposedType)
            {
                return genericName;
            }

            return string.Empty;
        }

        /// <summary>
        /// Follows pointer, array, qualification, and typedef wrappers to classify a primitive carrier.
        /// </summary>
        /// <param name="cppType">
        /// The borrowed parsed type shape.
        /// </param>
        /// <param name="isPointer">
        /// Whether pointer-like wrapping was already traversed; this does not change the primitive classification.
        /// </param>
        /// <returns>
        /// The primitive category, or Void when no primitive carrier is reachable through supported wrappers.
        /// </returns>
        public static CppPrimitiveKind GetPrimitiveKind(
            this CppType cppType,
            bool isPointer = false
        ) {
            if (cppType is CppArrayType arrayType)
            {
                return GetPrimitiveKind(arrayType.elementType, true);
            }

            if (cppType is CppPointerType pointer)
            {
                return GetPrimitiveKind(pointer.elementType, true);
            }

            if (cppType is CppQualifiedType qualified)
            {
                return GetPrimitiveKind(qualified.elementType, isPointer);
            }

            if (cppType is CppTypedef typedef)
            {
                return GetPrimitiveKind(typedef.elementType, isPointer);
            }

            if (cppType is CppPrimitiveType primitive)
            {
                return primitive.kind;
            }

            return CppPrimitiveKind.Void;
        }
    }
}
