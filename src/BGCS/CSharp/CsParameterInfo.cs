using System;
using BGCS.Core.Collections;

namespace BGCS.CSharp
{
    using System.Collections.Generic;
    using System.Xml.Serialization;
    using BGCS.CppAst.Model.Types;
    using Newtonsoft.Json;

    /// <summary>
    /// Combines the default-value and managed marshalling projections selected for a parameter.
    /// </summary>
    [Flags]
    public enum ParameterFlags
    {
        /// <summary>
        /// No parameter projection flags are present.
        /// </summary>
        None = 0,
        /// <summary>
        /// The parameter has a default-value expression.
        /// </summary>
        Default = 1 << 0,
        /// <summary>
        /// The emitted parameter uses the out modifier.
        /// </summary>
        Out = 1 << 1,
        /// <summary>
        /// The emitted parameter uses the ref modifier.
        /// </summary>
        Ref = 1 << 2,
        /// <summary>
        /// The emitted parameter uses the in modifier.
        /// </summary>
        In = 1 << 3,
        /// <summary>
        /// The parameter is projected as a managed span.
        /// </summary>
        Span = 1 << 4,
        /// <summary>
        /// The parameter uses an unmanaged pointer carrier.
        /// </summary>
        Pointer = 1 << 5,
        /// <summary>
        /// The parameter is projected as a managed string.
        /// </summary>
        String = 1 << 6,
        /// <summary>
        /// The parameter is projected as a managed array.
        /// </summary>
        Array = 1 << 7,
        /// <summary>
        /// The parameter is projected as a managed Boolean.
        /// </summary>
        Bool = 1 << 8,
    }

    /// <summary>
    /// Retains a native source type and mutable managed marshalling descriptor while a function overload is being analyzed.
    /// </summary>
    public class CsParameterInfo : ICloneable<CsParameterInfo>
    {
        /// <summary>
        /// Retains the supplied native and managed descriptors, initializing absent modifier and attribute collections as empty.
        /// </summary>
        /// <param name="name">
        /// The escaped managed parameter identifier.
        /// </param>
        /// <param name="cppType">
        /// The native AST type retained for this analysis invocation.
        /// </param>
        /// <param name="type">
        /// The mutable managed type descriptor retained by this parameter.
        /// </param>
        /// <param name="modifiers">
        /// The modifier list retained by this parameter.
        /// </param>
        /// <param name="attributes">
        /// The managed attribute fragments retained in emission order.
        /// </param>
        /// <param name="direction">
        /// The native parameter flow used by marshalling analysis.
        /// </param>
        /// <param name="defaultValue">
        /// The default expression, or null when this parameter is not defaulted.
        /// </param>
        /// <param name="fieldName">
        /// The associated structure field name, or null when no field projection exists.
        /// </param>
        [JsonConstructor]
        public CsParameterInfo(
            string name,
            CppType cppType,
            CsType type,
            List<string> modifiers,
            List<string> attributes,
            Direction direction,
            string? defaultValue,
            string? fieldName
        ) {
            this.name = name;
            this.cppType = cppType;
            this.type = type;
            this.modifiers = modifiers;
            this.attributes = attributes;
            this.direction = direction;
            this.defaultValue = defaultValue;
            this.fieldName = fieldName;
        }

        /// <summary>
        /// Retains the supplied native and managed descriptors, initializing absent modifier and attribute collections as empty.
        /// </summary>
        /// <param name="name">
        /// The escaped managed parameter identifier.
        /// </param>
        /// <param name="cppType">
        /// The native AST type retained for this analysis invocation.
        /// </param>
        /// <param name="type">
        /// The mutable managed type descriptor retained by this parameter.
        /// </param>
        /// <param name="modifiers">
        /// The modifier list retained by this parameter.
        /// </param>
        /// <param name="attributes">
        /// The managed attribute fragments retained in emission order.
        /// </param>
        /// <param name="direction">
        /// The native parameter flow used by marshalling analysis.
        /// </param>
        public CsParameterInfo(
            string name,
            CppType cppType,
            CsType type,
            List<string> modifiers,
            List<string> attributes,
            Direction direction
        ) {
            this.name = name;
            this.cppType = cppType;
            this.type = type;
            this.modifiers = modifiers;
            this.attributes = attributes;
            this.direction = direction;
        }

        /// <summary>
        /// Retains the supplied native and managed descriptors, initializing absent modifier and attribute collections as empty.
        /// </summary>
        /// <param name="name">
        /// The escaped managed parameter identifier.
        /// </param>
        /// <param name="cppType">
        /// The native AST type retained for this analysis invocation.
        /// </param>
        /// <param name="type">
        /// The mutable managed type descriptor retained by this parameter.
        /// </param>
        /// <param name="direction">
        /// The native parameter flow used by marshalling analysis.
        /// </param>
        /// <param name="defaultValue">
        /// The default expression, or null when this parameter is not defaulted.
        /// </param>
        /// <param name="fieldName">
        /// The associated structure field name, or null when no field projection exists.
        /// </param>
        public CsParameterInfo(
            string name,
            CppType cppType,
            CsType type,
            Direction direction,
            string? defaultValue,
            string? fieldName
        ) {
            this.name = name;
            this.cppType = cppType;
            this.type = type;
            this.modifiers = new();
            this.attributes = new();
            this.direction = direction;
            this.defaultValue = defaultValue;
            this.fieldName = fieldName;
        }

        /// <summary>
        /// Retains the supplied native and managed descriptors, initializing absent modifier and attribute collections as empty.
        /// </summary>
        /// <param name="name">
        /// The escaped managed parameter identifier.
        /// </param>
        /// <param name="cppType">
        /// The native AST type retained for this analysis invocation.
        /// </param>
        /// <param name="type">
        /// The mutable managed type descriptor retained by this parameter.
        /// </param>
        /// <param name="direction">
        /// The native parameter flow used by marshalling analysis.
        /// </param>
        public CsParameterInfo(
            string name,
            CppType cppType,
            CsType type,
            Direction direction
        ) {
            this.name = name;
            this.cppType = cppType;
            this.type = type;
            this.modifiers = new();
            this.attributes = new();
            this.direction = direction;
        }

        /// <summary>
        /// Gets or sets the managed parameter identifier, including any keyword escape.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets the managed name with keyword escape characters removed.
        /// </summary>
        public string cleanName => this.name.Replace("@", string.Empty);
        /// <summary>
        /// Gets or sets the native AST type owned by the current analysis invocation; it is excluded from serialized metadata.
        /// </summary>

        [XmlIgnore]
        [JsonIgnore]
        public CppType cppType { get; set; }
        /// <summary>
        /// Gets or sets the mutable managed type and marshalling descriptor.
        /// </summary>
        public CsType type { get; set; }
        /// <summary>
        /// Gets or sets managed parameter modifiers in emission order.
        /// </summary>
        public List<string> modifiers { get; set; }
        /// <summary>
        /// Gets or sets managed parameter attribute fragments in emission order.
        /// </summary>
        public List<string> attributes { get; set; }
        /// <summary>
        /// Gets or sets the native flow direction used by marshalling analysis.
        /// </summary>
        public Direction direction { get; set; }
        /// <summary>
        /// Gets or sets the default expression, or null when the parameter must be explicitly supplied.
        /// </summary>
        public string? defaultValue { get; set; }
        /// <summary>
        /// Gets or sets the associated field identifier, or null when no structure field projection exists.
        /// </summary>
        public string? fieldName { get; set; }

        /// <summary>
        /// Computes the current default-value and marshalling flags from this parameter and its managed type descriptor.
        /// </summary>
        public ParameterFlags flags
        {
            get
            {
                var result = ParameterFlags.None;
                result |= this.defaultValue != null ? ParameterFlags.Default : ParameterFlags.None;
                result |= this.type.isOut ? ParameterFlags.Out : ParameterFlags.None;
                result |= this.type.isRef ? ParameterFlags.Ref : ParameterFlags.None;
                result |= this.type.isIn ? ParameterFlags.In : ParameterFlags.None;
                result |= this.type.isSpan ? ParameterFlags.Span : ParameterFlags.None;
                result |= this.type.isPointer ? ParameterFlags.Pointer : ParameterFlags.None;
                result |= this.type.isString ? ParameterFlags.String : ParameterFlags.None;
                result |= this.type.isArray ? ParameterFlags.Array : ParameterFlags.None;
                result |= this.type.isBool ? ParameterFlags.Bool : ParameterFlags.None;
                return result;
            }
        }

        /// <summary>
        /// Formats the managed type and parameter identifier for diagnostic display.
        /// </summary>
        /// <returns>
        /// The managed type spelling followed by the parameter name.
        /// </returns>
        public override string ToString()
        {
            return $"{this.type.name} {this.name}";
        }

        /// <summary>
        /// Clones managed descriptors and declaration lists while retaining the native AST type reference.
        /// </summary>
        /// <returns>
        /// A separately mutable managed parameter projection; its native AST source remains shared.
        /// </returns>
        public CsParameterInfo Clone()
        {
            return new CsParameterInfo(this.name, this.cppType, this.type.Clone(), this.modifiers.Clone(), this.attributes.Clone(), this.direction, this.defaultValue, this.fieldName);
        }

        /// <summary>
        /// Compares managed type conflicts and exact default expressions for overload deduplication.
        /// </summary>
        /// <param name="other">
        /// The parameter projection to compare; managed names do not affect this comparison.
        /// </param>
        /// <returns>
        /// True when types conflict and default expressions match; otherwise false.
        /// </returns>
        public bool Conflicts(CsParameterInfo other)
        {
            return this.type.Conflicts(other.type) && this.defaultValue == other.defaultValue;
        }
    }
}
