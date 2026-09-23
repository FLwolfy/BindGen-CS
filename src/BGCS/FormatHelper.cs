namespace BGCS
{
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using Microsoft.CodeAnalysis;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>FormatHelper</c>.
    /// </summary>
    public static class FormatHelper
    {
        /// <summary>
        /// Executes public operation <c>IsCaps</c>.
        /// </summary>
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
        /// Executes public operation <c>IsPointer</c>.
        /// </summary>
        public static bool IsPointer(this CppType type)
        {
            if (type is CppPointerType)
            {
                return true;
            }

            if (type is CppQualifiedType qualifiedType)
            {
                return IsPointer(qualifiedType.ElementType);
            }

            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsPointer</c>.
        /// </summary>
        public static bool IsPointer(this CppType type, ref int depth)
        {
            bool isPointer = false;
            CppType d = type;
            depth = 0;
            while (true)
            {
                if (d is CppPointerType pointer)
                {
                    depth++;
                    d = pointer.ElementType;
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
        /// Executes public operation <c>IsPointer</c>.
        /// </summary>
        public static bool IsPointer(this CppType type, ref int depth, out CppType pointerType)
        {
            bool isPointer = false;
            CppType d = type;
            depth = 0;
            while (true)
            {
                if (d is CppPointerType pointer)
                {
                    depth++;
                    d = pointer.ElementType;
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
        /// Executes public operation <c>IsPointerOf</c>.
        /// </summary>
        public static bool IsPointerOf(this CppType type, CppType pointer)
        {
            type = UnwrapAliases(type);
            pointer = UnwrapAliases(pointer);
            if (pointer is CppPointerType pointerType)
            {
                return UnwrapAliases(pointerType.ElementType).GetDisplayName() == type.GetDisplayName();
            }
            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsPointerOf</c>.
        /// </summary>
        public static bool IsPointerOf(this CppType type, CppType pointer, ref int depth)
        {
            type = UnwrapAliases(type);
            pointer = UnwrapAliases(pointer);
            if (pointer is CppPointerType pointerType)
            {
                CppType elementType = UnwrapAliases(pointerType.ElementType);
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
                        type = typedef.ElementType;
                        continue;
                    case CppQualifiedType qualified:
                        type = qualified.ElementType;
                        continue;
                    default:
                        return type;
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>IsType</c>.
        /// </summary>
        public static bool IsType(this CppType a, CppType b)
        {
            return a.GetDisplayName() == b.GetDisplayName();
        }

        /// <summary>
        /// Executes public operation <c>IsPrimitive</c>.
        /// </summary>
        public static bool IsPrimitive(this CppType cppType, [NotNullWhen(true)] out CppPrimitiveType? primitive)
        {
            if (cppType is CppPrimitiveType cppPrimitive)
            {
                primitive = cppPrimitive;
                return true;
            }

            if (cppType is CppTypedef cppTypedef)
            {
                return IsPrimitive(cppTypedef.ElementType, out primitive);
            }

            if (cppType is CppPointerType cppPointerType)
            {
                return IsPrimitive(cppPointerType.ElementType, out primitive);
            }

            primitive = null;

            return false;
        }

        /// <summary>
        /// Determines whether a typedef or qualified type ultimately aliases a native pointer.
        /// </summary>
        /// <param name="cppType">Type to inspect.</param>
        /// <returns><see langword="true"/> when the alias chain ends in a pointer type.</returns>
        public static bool IsPointerAlias(this CppType cppType)
        {
            while (cppType is CppTypedef typedef)
                cppType = typedef.ElementType;
            while (cppType is CppQualifiedType qualified)
                cppType = qualified.ElementType;
            return cppType is CppPointerType;
        }

        /// <summary>
        /// Executes public operation <c>IsUsedAsPointer</c>.
        /// </summary>
        public static bool IsUsedAsPointer(this CppClass cppClass, CppCompilation compilation, out List<int> depths)
        {
            depths = new List<int>();
            int depth = 0;
            for (int i = 0; i < compilation.Functions.Count; i++)
            {
                depth = 0;
                var func = compilation.Functions[i];
                if (IsPointerOf(cppClass, func.ReturnType, ref depth))
                {
                    if (!depths.Contains(depth))
                        depths.Add(depth);
                }

                for (int j = 0; j < func.Parameters.Count; j++)
                {
                    depth = 0;
                    var param = func.Parameters[j];
                    if (IsPointerOf(cppClass, param.Type, ref depth))
                    {
                        if (!depths.Contains(depth))
                            depths.Add(depth);
                    }
                }
            }

            for (int i = 0; i < compilation.Classes.Count; i++)
            {
                var cl = compilation.Classes[i];
                for (int j = 0; j < cl.Fields.Count; j++)
                {
                    depth = 0;
                    var field = cl.Fields[j];
                    if (IsPointerOf(cppClass, field.Type, ref depth))
                    {
                        if (!depths.Contains(depth))
                            depths.Add(depth);
                    }
                }
            }

            return depths.Count > 0;
        }

        /// <summary>
        /// Executes public operation <c>NormalizeEnumValue</c>.
        /// </summary>
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
        /// Executes public operation <c>NormalizeConstantValue</c>.
        /// </summary>
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

        private static bool TryCombineAdjacentCStringLiterals(string value, [NotNullWhen(true)] out string? result)
        {
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
        /// Executes public operation <c>IsConstantExpression</c>.
        /// </summary>
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
        /// Executes public operation <c>IsString</c>.
        /// </summary>
        public static bool IsString(this string name)
        {
            return name.StartsWith("\"") && name.EndsWith("\"");
        }

        /// <summary>
        /// Executes public operation <c>IsVoid</c>.
        /// </summary>
        public static bool IsVoid(this CppType cppType)
        {
            if (cppType is CppPrimitiveType type)
            {
                return type.Kind == CppPrimitiveKind.Void;
            }
            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsString</c>.
        /// </summary>
        public static bool IsString(this CppType cppType, CsCodeGeneratorConfig config, out CppPrimitiveKind stringKind, bool isPointer = false)
        {
            if (cppType is CppPointerType pointer && !isPointer)
            {
                return IsString(pointer.ElementType, config, out stringKind, true);
            }

            if (cppType is CppQualifiedType qualified)
            {
                return IsString(qualified.ElementType, config, out stringKind, isPointer);
            }

            if (cppType is CppTypedef typedef)
            {
                if (config.TypeMappings.TryGetValue(typedef.Name, out var type))
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
                return IsString(typedef.ElementType, config, out stringKind, isPointer);
            }

            if (isPointer && cppType is CppPrimitiveType primitive)
            {
                stringKind = primitive.Kind;
                return primitive.Kind == CppPrimitiveKind.WChar || primitive.Kind == CppPrimitiveKind.Char;
            }

            stringKind = CppPrimitiveKind.Void;
            return false;
        }

        /// <summary>
        /// Executes public operation <c>IsTemplateParameter</c>.
        /// </summary>
        public static bool IsTemplateParameter(this CppType type, CppFunction function)
        {
            if (type is CppPointerType pointer)
            {
                return IsTemplateParameter(pointer.ElementType, function);
            }

            if (type is CppReferenceType reference)
            {
                return IsTemplateParameter(reference.ElementType, function);
            }

            if (type is CppArrayType array)
            {
                return IsTemplateParameter(array.ElementType, function);
            }

            if (type is CppQualifiedType qualified)
            {
                return IsTemplateParameter(qualified.ElementType, function);
            }

            if (type is CppUnexposedType unexposed)
            {
                for (int i = 0; i < function.TemplateParameters.Count; i++)
                {
                    if (function.TemplateParameters[i] == unexposed)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetTemplateParameterCsName</c>.
        /// </summary>
        public static string GetTemplateParameterCsName(this CppType type, string genericName)
        {
            if (type is CppPointerType pointer)
            {
                return GetTemplateParameterCsName(pointer.ElementType, genericName) + "*";
            }

            if (type is CppReferenceType reference)
            {
                return GetTemplateParameterCsName(reference.ElementType, genericName) + "*";
            }

            if (type is CppArrayType array)
            {
                return GetTemplateParameterCsName(array.ElementType, genericName) + "*";
            }

            if (type is CppQualifiedType qualified)
            {
                return GetTemplateParameterCsName(qualified.ElementType, genericName);
            }

            if (type is CppUnexposedType)
            {
                return genericName;
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns computed data from <c>GetPrimitiveKind</c>.
        /// </summary>
        public static CppPrimitiveKind GetPrimitiveKind(this CppType cppType, bool isPointer = false)
        {
            if (cppType is CppArrayType arrayType)
            {
                return GetPrimitiveKind(arrayType.ElementType, true);
            }

            if (cppType is CppPointerType pointer)
            {
                return GetPrimitiveKind(pointer.ElementType, true);
            }

            if (cppType is CppQualifiedType qualified)
            {
                return GetPrimitiveKind(qualified.ElementType, isPointer);
            }

            if (cppType is CppTypedef typedef)
            {
                return GetPrimitiveKind(typedef.ElementType, isPointer);
            }

            if (cppType is CppPrimitiveType primitive)
            {
                return primitive.Kind;
            }

            return CppPrimitiveKind.Void;
        }
    }
}
