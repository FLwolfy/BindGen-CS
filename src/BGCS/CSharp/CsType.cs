using System;
using System.Linq;
using BGCS.Core.Collections;
using BGCS.Core.Text;

namespace BGCS.CSharp
{
    using System.Globalization;
    using BGCS.CppAst.Model.Types;
    using Newtonsoft.Json;

    /// <summary>
    /// Retains mutable managed type spelling and marshalling classifications while an overload is being analyzed.
    /// </summary>
    public class CsType : ICloneable<CsType>
    {
        /// <summary>
        /// Restores an explicitly supplied type classification without recomputing it.
        /// </summary>
        /// <param name="name">
        /// The complete managed type spelling, including pointer, array, or parameter modifiers.
        /// </param>
        /// <param name="cleanName">
        /// The previously classified element spelling; this constructor does not reclassify it.
        /// </param>
        /// <param name="isPointer">
        /// Whether the spelling contains an unmanaged pointer carrier.
        /// </param>
        /// <param name="isOut">
        /// Whether the parameter spelling starts with the out modifier.
        /// </param>
        /// <param name="isRef">
        /// Whether the parameter spelling starts with the ref modifier.
        /// </param>
        /// <param name="isSpan">
        /// Whether the spelling represents Span or ReadOnlySpan.
        /// </param>
        /// <param name="isString">
        /// Whether string marshalling is selected.
        /// </param>
        /// <param name="isPrimitive">
        /// The scalar classification retained for overload planning.
        /// </param>
        /// <param name="isVoid">
        /// Whether the type represents a void return.
        /// </param>
        /// <param name="isBool">
        /// Whether Boolean marshalling is selected.
        /// </param>
        /// <param name="isArray">
        /// Whether managed array marshalling is selected.
        /// </param>
        /// <param name="isEnum">
        /// Whether the carrier represents an enum.
        /// </param>
        /// <param name="stringType">
        /// The selected string encoding category.
        /// </param>
        /// <param name="primitiveType">
        /// The primitive carrier category; native kinds are mapped before spelling classification.
        /// </param>
        [JsonConstructor]
        public CsType(
            string name,
            string cleanName,
            bool isPointer,
            bool isOut,
            bool isRef,
            bool isSpan,
            bool isString,
            bool isPrimitive,
            bool isVoid,
            bool isBool,
            bool isArray,
            bool isEnum,
            CsStringType stringType,
            CsPrimitiveType primitiveType
        ) {
            this.name = name;
            this.cleanName = cleanName;
            this.isPointer = isPointer;
            this.isOut = isOut;
            this.isRef = isRef;
            this.isSpan = isSpan;
            this.isString = isString;
            this.isPrimitive = isPrimitive;
            this.isVoid = isVoid;
            this.isBool = isBool;
            this.isArray = isArray;
            this.isEnum = isEnum;
            this.stringType = stringType;
            this.primitiveType = primitiveType;
        }

        /// <summary>
        /// Classifies managed spelling using the supplied primitive carrier category.
        /// </summary>
        /// <param name="name">
        /// The complete managed type spelling, including pointer, array, or parameter modifiers.
        /// </param>
        /// <param name="isPointer">
        /// Whether the spelling contains an unmanaged pointer carrier.
        /// </param>
        /// <param name="isRef">
        /// Whether the parameter spelling starts with the ref modifier.
        /// </param>
        /// <param name="isString">
        /// Whether string marshalling is selected.
        /// </param>
        /// <param name="isPrimitive">
        /// The scalar classification retained for overload planning.
        /// </param>
        /// <param name="isVoid">
        /// Whether the type represents a void return.
        /// </param>
        /// <param name="isArray">
        /// Whether managed array marshalling is selected.
        /// </param>
        /// <param name="primitiveType">
        /// The primitive carrier category; native kinds are mapped before spelling classification.
        /// </param>
        public CsType(
            string name,
            bool isPointer,
            bool isRef,
            bool isString,
            bool isPrimitive,
            bool isVoid,
            bool isArray,
            CsPrimitiveType primitiveType
        ) {
            this.name = name;
            this.isPointer = isPointer;
            this.isRef = isRef;
            this.isString = isString;
            this.isPrimitive = isPrimitive;
            this.isVoid = isVoid;
            this.isArray = isArray;
            this.primitiveType = primitiveType;
            this.cleanName = Classify();
        }

        /// <summary>
        /// Classifies managed spelling using the supplied primitive carrier category.
        /// </summary>
        /// <param name="name">
        /// The complete managed type spelling, including pointer, array, or parameter modifiers.
        /// </param>
        /// <param name="primitiveType">
        /// The primitive carrier category; native kinds are mapped before spelling classification.
        /// </param>
        public CsType(
            string name,
            CsPrimitiveType primitiveType
        ) {
            this.name = name;
            this.primitiveType = primitiveType;
            this.cleanName = Classify();
        }

        /// <summary>
        /// Classifies managed spelling using the supplied primitive carrier category.
        /// </summary>
        /// <param name="name">
        /// The complete managed type spelling, including pointer, array, or parameter modifiers.
        /// </param>
        /// <param name="primitiveType">
        /// The primitive carrier category; native kinds are mapped before spelling classification.
        /// </param>
        public CsType(
            string name,
            CppPrimitiveKind primitiveType
        ) {
            this.name = name;
            this.primitiveType = Map(primitiveType);
            this.cleanName = Classify();
        }

        /// <summary>
        /// Classifies managed spelling using the supplied primitive carrier category.
        /// </summary>
        /// <param name="name">
        /// The complete managed type spelling, including pointer, array, or parameter modifiers.
        /// </param>
        /// <param name="isEnum">
        /// Whether the carrier represents an enum.
        /// </param>
        /// <param name="primitiveType">
        /// The primitive carrier category; native kinds are mapped before spelling classification.
        /// </param>
        public CsType(
            string name,
            bool isEnum,
            CppPrimitiveKind primitiveType
        ) {
            this.name = name;
            this.primitiveType = Map(primitiveType);
            this.isEnum = isEnum;
            this.cleanName = Classify();
        }

        /// <summary>
        /// Gets or sets the complete managed spelling; callers must invoke Classify after changing it.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the cached element spelling produced by classification.
        /// </summary>
        public string cleanName { get; set; }
        /// <summary>
        /// Gets or sets whether unmanaged pointer marshalling is selected.
        /// </summary>
        public bool isPointer { get; set; }
        /// <summary>
        /// Gets or sets whether this parameter uses out projection.
        /// </summary>
        public bool isOut { get; set; }
        /// <summary>
        /// Gets or sets whether this parameter uses ref projection.
        /// </summary>
        public bool isRef { get; set; }
        /// <summary>
        /// Gets or sets whether this parameter uses read-only in projection.
        /// </summary>
        public bool isIn { get; set; }
        /// <summary>
        /// Gets or sets whether Span or ReadOnlySpan marshalling is selected.
        /// </summary>
        public bool isSpan { get; set; }
        /// <summary>
        /// Gets or sets whether managed string marshalling is selected.
        /// </summary>
        public bool isString { get; set; }
        /// <summary>
        /// Gets or sets the scalar classification used by overload planning.
        /// </summary>
        public bool isPrimitive { get; set; }
        /// <summary>
        /// Gets or sets whether the type has a void carrier.
        /// </summary>
        public bool isVoid { get; set; }
        /// <summary>
        /// Gets or sets whether managed Boolean marshalling is selected.
        /// </summary>
        public bool isBool { get; set; }
        /// <summary>
        /// Gets or sets whether managed array marshalling is selected.
        /// </summary>
        public bool isArray { get; set; }
        /// <summary>
        /// Gets or sets whether the carrier represents an enum.
        /// </summary>
        public bool isEnum { get; set; }
        /// <summary>
        /// Gets or sets the string encoding category used by marshalling.
        /// </summary>
        public CsStringType stringType { get; set; }
        /// <summary>
        /// Gets or sets the primitive carrier category used by type mapping.
        /// </summary>
        public CsPrimitiveType primitiveType { get; set; }
        /// <summary>
        /// Gets whether the parameter has either by-reference modifier that conflicts under overload normalization.
        /// </summary>
        public bool isRefOrIn => this.isRef || this.isIn;

        /// <summary>
        /// Recognizes supported C# primitive spellings after trimming whitespace and trailing pointer stars.
        /// </summary>
        /// <param name="name">
        /// The managed type spelling to inspect.
        /// </param>
        /// <returns>
        /// True for a recognized primitive carrier; otherwise false.
        /// </returns>
        public static bool IsKnownPrimitive(string name)
        {
            ReadOnlySpan<char> baseName = name.AsSpan().Trim();
            while (baseName.EndsWith("*", StringComparison.Ordinal))
                baseName = baseName[..^1].TrimEnd();
            return baseName is "void" or "bool" or "byte" or "sbyte" or "char" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "float" or "double" or "nint" or "nuint";
        }

        /// <summary>
        /// Recomputes marshalling flags from the current spelling and selects string encoding from the primitive carrier.
        /// </summary>
        /// <returns>
        /// The element spelling with parameter, pointer, array, or span syntax removed; the cached cleanName is not assigned automatically.
        /// </returns>
        public string Classify()
        {
            this.isRef = this.name.StartsWith("ref ");
            this.isIn = this.name.StartsWith("in ");
            this.isSpan = this.name.StartsWith("ReadOnlySpan<") || this.name.StartsWith("Span<");
            this.isOut = this.name.StartsWith("out ");
            this.isArray = this.name.Contains("[]");
            this.isPointer = this.name.Contains('*');
            this.isBool = this.name.Contains("bool");
            this.isString = this.name.Contains("string");
            this.isVoid = this.name.StartsWith("void");
            this.isPrimitive = !this.isOut && !this.isRef && !this.isIn && !this.isArray && !this.isPointer && !this.isArray && !this.isString;
            if (this.isString)
            {
                if (this.primitiveType == CsPrimitiveType.Byte)
                {
                    this.stringType = CsStringType.StringUTF8;
                }

                if (this.primitiveType == CsPrimitiveType.Char)
                {
                    this.stringType = CsStringType.StringUTF16;
                }
            }

            if (this.isRef)
            {
                return this.name.Replace("ref ", string.Empty);
            }

            if (this.isOut)
            {
                return this.name.Replace("out ", string.Empty);
            }

            if (this.isIn)
            {
                return this.name.Replace("in ", string.Empty);
            }

            if (this.isSpan)
            {
                var temp = this.name.AsSpan();
                temp = temp.StartsWith("ReadOnlySpan<") ? temp["ReadOnlySpan<".Length..] : temp;
                temp = temp.StartsWith("Span<") ? temp["Span<".Length..] : temp;
                temp = temp.TrimEndFirstOccurrence('>');
                return temp.ToString();
            }
            else if (this.isArray)
            {
                return this.name.Replace("[]", string.Empty);
            }
            else if (this.isPointer)
            {
                return this.name.Replace("*", string.Empty);
            }
            else
            {
                return this.name;
            }
        }

        /// <summary>
        /// Maps a native primitive category to its conventional managed carrier; ABI-dependent long widths require the target-aware mapping layer.
        /// </summary>
        /// <param name="kind">
        /// The native primitive category to map.
        /// </param>
        /// <returns>
        /// The conventional managed carrier category.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// The native category has no conventional mapping.
        /// </exception>
        public static CsPrimitiveType Map(CppPrimitiveKind kind)
        {
            return kind switch
            {
                CppPrimitiveKind.Void => CsPrimitiveType.Void,
                CppPrimitiveKind.Bool => CsPrimitiveType.Bool,
                CppPrimitiveKind.WChar => CsPrimitiveType.Char,
                CppPrimitiveKind.Char => CsPrimitiveType.Byte,
                CppPrimitiveKind.Short => CsPrimitiveType.Short,
                CppPrimitiveKind.Int => CsPrimitiveType.Int,
                CppPrimitiveKind.LongLong => CsPrimitiveType.Long,
                CppPrimitiveKind.UnsignedChar => CsPrimitiveType.Byte,
                CppPrimitiveKind.UnsignedShort => CsPrimitiveType.UShort,
                CppPrimitiveKind.UnsignedInt => CsPrimitiveType.UInt,
                CppPrimitiveKind.UnsignedLongLong => CsPrimitiveType.ULong,
                CppPrimitiveKind.Float => CsPrimitiveType.Float,
                CppPrimitiveKind.Double => CsPrimitiveType.Double,
                CppPrimitiveKind.LongDouble => CsPrimitiveType.Double,
                CppPrimitiveKind.UnsignedLong => CsPrimitiveType.UInt,
                CppPrimitiveKind.Long => CsPrimitiveType.Int,
                _ => throw new NotSupportedException($"The kind '{kind}' is not supported"),
            };
        }

        /// <summary>
        /// Returns the complete managed type spelling for emission.
        /// </summary>
        /// <returns>
        /// The current name, including its parameter and pointer modifiers.
        /// </returns>
        public override string ToString()
        {
            return this.name;
        }

        /// <summary>
        /// Copies the complete classification into a separately mutable type descriptor.
        /// </summary>
        /// <returns>
        /// A new descriptor preserving all flags, including read-only in projection.
        /// </returns>
        public CsType Clone()
        {
            return new CsType(this.name, this.cleanName, this.isPointer, this.isOut, this.isRef, this.isSpan, this.isString, this.isPrimitive, this.isVoid, this.isBool, this.isArray, this.isEnum, this.stringType, this.primitiveType) { isIn = this.isIn };
        }

        /// <summary>
        /// Removes leading ref or in syntax for overload conflict comparison.
        /// </summary>
        /// <returns>
        /// A trimmed span borrowing the current name string; out syntax remains distinct.
        /// </returns>
        public ReadOnlySpan<char> GetNormalizedName()
        {
            var nameNormalized = this.name.AsSpan();
            if (this.isRef)
                nameNormalized = nameNormalized["ref ".Length..];
            if (this.isIn)
                nameNormalized = nameNormalized["in ".Length..];
            return nameNormalized.Trim();
        }

        /// <summary>
        /// Compares managed spellings using the shared ref/in overload conflict rule.
        /// </summary>
        /// <param name="other">
        /// The type descriptor to compare.
        /// </param>
        /// <returns>
        /// True for identical spellings or equal ref/in element spellings; otherwise false.
        /// </returns>
        public bool Conflicts(CsType other)
        {
            if (this.isRefOrIn && other.isRefOrIn)
            {
                return GetNormalizedName().SequenceEqual(other.GetNormalizedName());
            }

            return this.name == other.name;
        }

        private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;
        /// <summary>
        /// Hashes the managed spelling using the same ref/in normalization used for overload conflict comparison.
        /// </summary>
        /// <returns>
        /// The in-process conflict hash; it is not a persistent type identity.
        /// </returns>
        public int GetConflictHashCode()
        {
            HashCode code = new();
            if (this.isRefOrIn)
            {
                code.Add("ref");
                code.Add(CompareInfo.GetHashCode(GetNormalizedName(), CompareOptions.None));
            }
            else
            {
                code.Add(CompareInfo.GetHashCode(this.name, CompareOptions.None));
            }

            return code.ToHashCode();
        }
    }
}
