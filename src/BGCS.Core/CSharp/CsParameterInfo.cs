namespace BGCS.Core.CSharp
{
    using CppAst;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using System.Xml.Serialization;
    using BGCS.CppAst.Model.Types;

    [Flags]
    /// <summary>
    /// Defines values for <c>ParameterFlags</c>.
    /// </summary>
    public enum ParameterFlags
    {
        None = 0,
        Default = 1 << 0,
        Out = 1 << 1,
        Ref = 1 << 2,
        In = 1 << 3,
        Span = 1 << 4,
        Pointer = 1 << 5,
        String = 1 << 6,
        Array = 1 << 7,
        Bool = 1 << 8,
    }

    /// <summary>
    /// Defines the public class <c>CsParameterInfo</c>.
    /// </summary>
    public class CsParameterInfo : ICloneable<CsParameterInfo>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsParameterInfo"/>.
        /// </summary>
        public CsParameterInfo(string name, CppType cppType, CsType type, List<string> modifiers, List<string> attributes, Direction direction, string? defaultValue, string? fieldName)
        {
            Name = name;
            CppType = cppType;
            Type = type;
            Modifiers = modifiers;
            Attributes = attributes;
            Direction = direction;
            DefaultValue = defaultValue;
            FieldName = fieldName;
        }

        /// <summary>
        /// Executes public operation <c>CsParameterInfo</c>.
        /// </summary>
        public CsParameterInfo(string name, CppType cppType, CsType type, List<string> modifiers, List<string> attributes, Direction direction)
        {
            Name = name;
            CppType = cppType;
            Type = type;
            Modifiers = modifiers;
            Attributes = attributes;
            Direction = direction;
        }

        /// <summary>
        /// Executes public operation <c>CsParameterInfo</c>.
        /// </summary>
        public CsParameterInfo(string name, CppType cppType, CsType type, Direction direction, string? defaultValue, string? fieldName)
        {
            Name = name;
            CppType = cppType;
            Type = type;
            Modifiers = new();
            Attributes = new();
            Direction = direction;
            DefaultValue = defaultValue;
            FieldName = fieldName;
        }

        /// <summary>
        /// Executes public operation <c>CsParameterInfo</c>.
        /// </summary>
        public CsParameterInfo(string name, CppType cppType, CsType type, Direction direction)
        {
            Name = name;
            CppType = cppType;
            Type = type;
            Modifiers = new();
            Attributes = new();
            Direction = direction;
        }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Executes public operation <c>Replace</c>.
        /// </summary>
        public string CleanName => Name.Replace("@", string.Empty);

        [XmlIgnore]
        [JsonIgnore]
        /// <summary>
        /// Gets or sets <c>CppType</c>.
        /// </summary>
        public CppType CppType { get; set; }

        /// <summary>
        /// Gets or sets <c>Type</c>.
        /// </summary>
        public CsType Type { get; set; }

        /// <summary>
        /// Gets or sets <c>Modifiers</c>.
        /// </summary>
        public List<string> Modifiers { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string> Attributes { get; set; }

        /// <summary>
        /// Gets or sets <c>Direction</c>.
        /// </summary>
        public Direction Direction { get; set; }

        /// <summary>
        /// Gets or sets <c>DefaultValue</c>.
        /// </summary>
        public string? DefaultValue { get; set; }

        /// <summary>
        /// Gets or sets <c>FieldName</c>.
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// Exposes public member <c>Flags</c>.
        /// </summary>
        public ParameterFlags Flags
        {
            get
            {
                var result = ParameterFlags.None;
                result |= DefaultValue != null ? ParameterFlags.Default : ParameterFlags.None;
                result |= Type.IsOut ? ParameterFlags.Out : ParameterFlags.None;
                result |= Type.IsRef ? ParameterFlags.Ref : ParameterFlags.None;
                result |= Type.IsIn ? ParameterFlags.In : ParameterFlags.None;
                result |= Type.IsSpan ? ParameterFlags.Span : ParameterFlags.None;
                result |= Type.IsPointer ? ParameterFlags.Pointer : ParameterFlags.None;
                result |= Type.IsString ? ParameterFlags.String : ParameterFlags.None;
                result |= Type.IsArray ? ParameterFlags.Array : ParameterFlags.None;
                result |= Type.IsBool ? ParameterFlags.Bool : ParameterFlags.None;
                return result;
            }
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"{Type.Name} {Name}";
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsParameterInfo Clone()
        {
            return new CsParameterInfo(Name, CppType, Type.Clone(), Modifiers.Clone(), Attributes.Clone(), Direction, DefaultValue, FieldName);
        }

        /// <summary>
        /// Executes public operation <c>Conflicts</c>.
        /// </summary>
        public bool Conflicts(CsParameterInfo other)
        {
            return Type.Conflicts(other.Type) && DefaultValue == other.DefaultValue;
        }
    }
}
