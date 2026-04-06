namespace BGCS.Core.CSharp
{
    using CppAst;
    using BGCS.CppAst.Model.Types;
    using Newtonsoft.Json;
    using System.Globalization;

    /// <summary>
    /// Defines the public class <c>CsType</c>.
    /// </summary>
    public class CsType : ICloneable<CsType>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsType"/>.
        /// </summary>
        public CsType(string name, string cleanName, bool isPointer, bool isOut, bool isRef, bool isSpan, bool isString, bool isPrimitive, bool isVoid, bool isBool, bool isArray, bool isEnum, CsStringType stringType, CsPrimitiveType primitiveType)
        {
            Name = name;
            CleanName = cleanName;
            IsPointer = isPointer;
            IsOut = isOut;
            IsRef = isRef;
            IsSpan = isSpan;
            IsString = isString;
            IsPrimitive = isPrimitive;
            IsVoid = isVoid;
            IsBool = isBool;
            IsArray = isArray;
            IsEnum = isEnum;
            StringType = stringType;
            PrimitiveType = primitiveType;
        }

        /// <summary>
        /// Executes public operation <c>CsType</c>.
        /// </summary>
        public CsType(string name, bool isPointer, bool isRef, bool isString, bool isPrimitive, bool isVoid, bool isArray, CsPrimitiveType primitiveType)
        {
            Name = name;
            IsPointer = isPointer;
            IsRef = isRef;
            IsString = isString;
            IsPrimitive = isPrimitive;
            IsVoid = isVoid;
            IsArray = isArray;
            PrimitiveType = primitiveType;
            CleanName = Classify();
        }

        /// <summary>
        /// Executes public operation <c>CsType</c>.
        /// </summary>
        public CsType(string name, CsPrimitiveType primitiveType)
        {
            Name = name;
            PrimitiveType = primitiveType;
            CleanName = Classify();
        }

        /// <summary>
        /// Executes public operation <c>CsType</c>.
        /// </summary>
        public CsType(string name, CppPrimitiveKind primitiveType)
        {
            Name = name;
            PrimitiveType = Map(primitiveType);
            CleanName = Classify();
        }

        /// <summary>
        /// Executes public operation <c>CsType</c>.
        /// </summary>
        public CsType(string name, bool isEnum, CppPrimitiveKind primitiveType)
        {
            Name = name;
            PrimitiveType = Map(primitiveType);
            IsEnum = isEnum;
            CleanName = Classify();
        }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>CleanName</c>.
        /// </summary>
        public string CleanName { get; set; }

        /// <summary>
        /// Gets or sets <c>IsPointer</c>.
        /// </summary>
        public bool IsPointer { get; set; }

        /// <summary>
        /// Gets or sets <c>IsOut</c>.
        /// </summary>
        public bool IsOut { get; set; }

        /// <summary>
        /// Gets or sets <c>IsRef</c>.
        /// </summary>
        public bool IsRef { get; set; }

        /// <summary>
        /// Gets or sets <c>IsIn</c>.
        /// </summary>
        public bool IsIn { get; set; }

        /// <summary>
        /// Gets or sets <c>IsSpan</c>.
        /// </summary>
        public bool IsSpan { get; set; }

        /// <summary>
        /// Gets or sets <c>IsString</c>.
        /// </summary>
        public bool IsString { get; set; }

        /// <summary>
        /// Gets or sets <c>IsPrimitive</c>.
        /// </summary>
        public bool IsPrimitive { get; set; }

        /// <summary>
        /// Gets or sets <c>IsVoid</c>.
        /// </summary>
        public bool IsVoid { get; set; }

        /// <summary>
        /// Gets or sets <c>IsBool</c>.
        /// </summary>
        public bool IsBool { get; set; }

        /// <summary>
        /// Gets or sets <c>IsArray</c>.
        /// </summary>
        public bool IsArray { get; set; }

        /// <summary>
        /// Gets or sets <c>IsEnum</c>.
        /// </summary>
        public bool IsEnum { get; set; }

        /// <summary>
        /// Gets or sets <c>StringType</c>.
        /// </summary>
        public CsStringType StringType { get; set; }

        /// <summary>
        /// Gets or sets <c>PrimitiveType</c>.
        /// </summary>
        public CsPrimitiveType PrimitiveType { get; set; }

        /// <summary>
        /// Exposes public member <c>IsIn</c>.
        /// </summary>
        public bool IsRefOrIn => IsRef || IsIn;

        /// <summary>
        /// Executes public operation <c>IsKnownPrimitive</c>.
        /// </summary>
        public static bool IsKnownPrimitive(string name)
        {
            if (name.StartsWith("void"))
                return true;
            if (name.StartsWith("bool"))
                return true;
            if (name.StartsWith("byte"))
                return true;
            if (name.StartsWith("sbyte"))
                return true;
            if (name.StartsWith("char"))
                return true;
            if (name.StartsWith("short"))
                return true;
            if (name.StartsWith("ushort"))
                return true;
            if (name.StartsWith("int"))
                return true;
            if (name.StartsWith("uint"))
                return true;
            if (name.StartsWith("long"))
                return true;
            if (name.StartsWith("ulong"))
                return true;
            if (name.StartsWith("float"))
                return true;
            if (name.StartsWith("double"))
                return true;
            if (name.StartsWith("Vector2"))
                return true;
            if (name.StartsWith("Vector3"))
                return true;
            if (name.StartsWith("Vector4"))
                return true;
            return false;
        }

        /// <summary>
        /// Executes public operation <c>Classify</c>.
        /// </summary>
        public string Classify()
        {
            IsRef = Name.StartsWith("ref ");
            IsIn = Name.StartsWith("in ");
            IsSpan = Name.StartsWith("ReadOnlySpan<") || Name.StartsWith("Span<");
            IsOut = Name.StartsWith("out ");
            IsArray = Name.Contains("[]");
            IsPointer = Name.Contains('*');
            IsBool = Name.Contains("bool");
            IsString = Name.Contains("string");
            IsVoid = Name.StartsWith("void");

            IsPrimitive = !IsOut && !IsRef && !IsIn && !IsArray && !IsPointer && !IsArray && !IsString;

            if (IsString)
            {
                if (PrimitiveType == CsPrimitiveType.Byte)
                {
                    StringType = CsStringType.StringUTF8;
                }
                if (PrimitiveType == CsPrimitiveType.Char)
                {
                    StringType = CsStringType.StringUTF16;
                }
            }

            if (IsRef)
            {
                return Name.Replace("ref ", string.Empty);
            }
            if (IsOut)
            {
                return Name.Replace("out ", string.Empty);
            }
            if (IsIn)
            {
                return Name.Replace("in ", string.Empty);
            }
            if (IsSpan)
            {
                var temp = Name.AsSpan();

                temp = temp.StartsWith("ReadOnlySpan<") ? temp["ReadOnlySpan<".Length..] : temp;
                temp = temp.StartsWith("Span<") ? temp["Span<".Length..] : temp;
                temp = temp.TrimEndFirstOccurrence('>');

                return temp.ToString();
            }
            else if (IsArray)
            {
                return Name.Replace("[]", string.Empty);
            }
            else if (IsPointer)
            {
                return Name.Replace("*", string.Empty);
            }
            else
            {
                return Name;
            }
        }

        /// <summary>
        /// Executes public operation <c>Map</c>.
        /// </summary>
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
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsType Clone()
        {
            return new CsType(Name, CleanName, IsPointer, IsOut, IsRef, IsSpan, IsString, IsPrimitive, IsVoid, IsBool, IsArray, IsEnum, StringType, PrimitiveType);
        }

        /// <summary>
        /// Returns computed data from <c>GetNormalizedName</c>.
        /// </summary>
        public ReadOnlySpan<char> GetNormalizedName()
        {
            var nameNormalized = Name.AsSpan();
            if (IsRef)
                nameNormalized = nameNormalized["ref ".Length..];
            if (IsIn)
                nameNormalized = nameNormalized["in ".Length..];
            return nameNormalized.Trim();
        }

        /// <summary>
        /// Executes public operation <c>Conflicts</c>.
        /// </summary>
        public bool Conflicts(CsType other)
        {
            if (IsRefOrIn && other.IsRefOrIn)
            {
                return GetNormalizedName().SequenceEqual(other.GetNormalizedName());
            }
            return Name == other.Name;
        }

        private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;

        /// <summary>
        /// Returns computed data from <c>GetConflictHashCode</c>.
        /// </summary>
        public int GetConflictHashCode()
        {
            HashCode code = new();
            if (IsRefOrIn)
            {
                code.Add("ref");
                code.Add(CompareInfo.GetHashCode(GetNormalizedName(), CompareOptions.None));
            }
            else
            {
                code.Add(CompareInfo.GetHashCode(Name, CompareOptions.None));
            }
            return code.ToHashCode();
        }
    }
}
