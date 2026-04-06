namespace BGCS.Core.CSharp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsFunctionVariation</c>.
    /// </summary>
    public class CsFunctionVariation : ICsFunction, ICloneable<CsFunctionVariation>, IHasIdentifier
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsFunctionVariation"/>.
        /// </summary>
        public CsFunctionVariation(string identifier, string exportedName, string name, string structName, CsFunctionKind kind, CsType returnType, List<CsParameterInfo> parameters, List<CsGenericParameterInfo> genericParameters, List<string> modifiers, List<string> attributes)
        {
            Identifier = identifier;
            ExportedName = exportedName;
            Name = name;

            StructName = structName;
            Kind = kind;
            ReturnType = returnType;
            Parameters = parameters;
            GenericParameters = genericParameters;
            Modifiers = modifiers;
            Attributes = attributes;
        }

        /// <summary>
        /// Executes public operation <c>CsFunctionVariation</c>.
        /// </summary>
        public CsFunctionVariation(string identifier, string exportedName, string name, string structName, CsFunctionKind kind, CsType returnType)
        {
            Identifier = identifier;
            ExportedName = exportedName;
            Name = name;
            StructName = structName;
            Kind = kind;
            ReturnType = returnType;
            Parameters = new();
            GenericParameters = new();
            Modifiers = new();
            Attributes = new();
        }

        /// <summary>
        /// Gets or sets <c>Identifier</c>.
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>StructName</c>.
        /// </summary>
        public string StructName { get; set; }

        /// <summary>
        /// Gets or sets <c>Kind</c>.
        /// </summary>
        public CsFunctionKind Kind { get; set; }

        /// <summary>
        /// Exposes public member <c>0</c>.
        /// </summary>
        public bool IsGeneric => GenericParameters.Count > 0;

        /// <summary>
        /// Gets or sets <c>ReturnType</c>.
        /// </summary>
        public CsType ReturnType { get; set; }

        /// <summary>
        /// Gets or sets <c>Parameters</c>.
        /// </summary>
        public List<CsParameterInfo> Parameters { get; set; }

        /// <summary>
        /// Gets or sets <c>GenericParameters</c>.
        /// </summary>
        public List<CsGenericParameterInfo> GenericParameters { get; set; }

        /// <summary>
        /// Gets or sets <c>Modifiers</c>.
        /// </summary>
        public List<string> Modifiers { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string> Attributes { get; set; }

        #region IDs

        protected virtual string BuildFunctionSignature(CsFunctionVariation variation, bool useAttributes, bool useNames, bool conflictResolution, WriteFunctionFlags flags)
        {
            int offset = flags == WriteFunctionFlags.None ? 0 : 1;
            StringBuilder sb = new();
            bool isFirst = true;

            if (flags == WriteFunctionFlags.Extension)
            {
                isFirst = false;
                var first = variation.Parameters[0];
                if (useNames)
                {
                    sb.Append($"this {first.Type} {first.Name}");
                }
                else
                {
                    sb.Append($"this {first.Type}");
                }
            }

            for (int i = offset; i < variation.Parameters.Count; i++)
            {
                var param = variation.Parameters[i];

                if (param.DefaultValue != null)
                    continue;

                if (!isFirst)
                    sb.Append(", ");

                if (useAttributes)
                {
                    sb.Append($"{string.Join(" ", param.Attributes)} ");
                }

                if (conflictResolution && param.Type.IsRefOrIn)
                {
                    sb.Append("ref ");
                    sb.Append(param.Type.GetNormalizedName());
                }
                else
                {
                    sb.Append($"{param.Type}");
                }

                if (useNames)
                {
                    sb.Append($" {param.Name}");
                }

                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Executes public operation <c>BuildFunctionHeaderId</c>.
        /// </summary>
        public string BuildFunctionHeaderId(WriteFunctionFlags flags)
        {
            string signature = BuildFunctionSignature(this, false, false, true, flags);
            return Identifier = $"{Name}({signature})";
        }

        /// <summary>
        /// Executes public operation <c>BuildFunctionHeaderId</c>.
        /// </summary>
        public string BuildFunctionHeaderId(string alias, WriteFunctionFlags flags)
        {
            string signature = BuildFunctionSignature(this, false, false, true, flags);
            return Identifier = $"{alias}({signature})";
        }

        /// <summary>
        /// Executes public operation <c>BuildFunctionHeader</c>.
        /// </summary>
        public string BuildFunctionHeader(CsType csReturnType, WriteFunctionFlags flags, bool generateMetadata)
        {
            string signature = BuildFunctionSignature(this, generateMetadata, true, false, flags);
            if (IsGeneric)
            {
                return Identifier = $"{csReturnType.Name} {Name}<{BuildGenericSignature()}>({signature}) {BuildGenericConstraint()}";
            }
            else
            {
                return Identifier = $"{csReturnType.Name} {Name}({signature})";
            }
        }

        /// <summary>
        /// Executes public operation <c>BuildFunctionHeader</c>.
        /// </summary>
        public string BuildFunctionHeader(string alias, CsType csReturnType, WriteFunctionFlags flags, bool generateMetadata)
        {
            string signature = BuildFunctionSignature(this, generateMetadata, true, false, flags);
            if (IsGeneric)
            {
                return Identifier = $"{csReturnType.Name} {alias}<{BuildGenericSignature()}>({signature}) {BuildGenericConstraint()}";
            }
            else
            {
                return Identifier = $"{csReturnType.Name} {alias}({signature})";
            }
        }

        /// <summary>
        /// Executes public operation <c>BuildFunctionOverload</c>.
        /// </summary>
        public string BuildFunctionOverload(WriteFunctionFlags flags)
        {
            string signature = BuildFunctionOverload(this, flags);
            if (IsGeneric)
            {
                return $"{Name}<{BuildGenericSignature()}>({signature})";
            }
            else
            {
                return $"{Name}({signature})";
            }
        }

        protected virtual string BuildFunctionOverload(CsFunctionVariation variation, WriteFunctionFlags flags)
        {
            int offset = flags == WriteFunctionFlags.None ? 0 : 1;
            StringBuilder sb = new();
            bool isFirst = true;

            if (flags == WriteFunctionFlags.Extension)
            {
                isFirst = false;
                var first = variation.Parameters[0];
                sb.Append("this");

                sb.Append($" {first.Name}");
            }

            for (int i = offset; i < variation.Parameters.Count; i++)
            {
                bool written = false;
                var param = variation.Parameters[i];

                if (param.DefaultValue != null)
                    continue;

                if (!isFirst)
                    sb.Append(", ");

                if (param.Type.IsRef)
                {
                    sb.Append("ref");
                    written = true;
                }

                if (param.Type.IsOut)
                {
                    sb.Append("out");
                    written = true;
                }

                if (written) sb.Append(' ');
                sb.Append(param.Name);

                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Executes public operation <c>BuildConstructorSignatureIdentifier</c>.
        /// </summary>
        public string BuildConstructorSignatureIdentifier()
        {
            return Identifier = $"{StructName}({BuildConstructorSignature(false, false, false)})";
        }

        #endregion IDs

        /// <summary>
        /// Executes public operation <c>BuildFullConstructorSignature</c>.
        /// </summary>
        public string BuildFullConstructorSignature(bool generateMetadata)
        {
            return $"{StructName}({BuildConstructorSignature(generateMetadata)})";
        }

        /// <summary>
        /// Executes public operation <c>BuildFullSignature</c>.
        /// </summary>
        public string BuildFullSignature()
        {
            return $"{ReturnType.Name} {Name}{(IsGeneric ? $"<{BuildGenericSignature()}>" : string.Empty)}({BuildSignature()}) {BuildGenericConstraint()}";
        }

        /// <summary>
        /// Executes public operation <c>BuildFullExtensionSignature</c>.
        /// </summary>
        public string BuildFullExtensionSignature(string type, string name)
        {
            return $"{ReturnType.Name} {Name}{(IsGeneric ? $"<{BuildGenericSignature()}>" : string.Empty)}({BuildExtensionSignature(type, name)}) {BuildGenericConstraint()}";
        }

        /// <summary>
        /// Executes public operation <c>BuildSignature</c>.
        /// </summary>
        public string BuildSignature(bool useAttributes = true, bool useNames = true)
        {
            StringBuilder sb = new();
            bool isFirst = true;
            for (int i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                var writeAttr = useAttributes && param.Attributes.Count > 0;

                if (param.DefaultValue != null)
                    continue;

                if (!isFirst)
                    sb.Append(", ");

                sb.Append($"{(writeAttr ? string.Join(" ", param.Attributes) + " " : string.Empty)}{param.Type}{(useNames ? " " + param.Name : string.Empty)}");
                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Executes public operation <c>BuildConstructorSignature</c>.
        /// </summary>
        public string BuildConstructorSignature(bool useAttributes = true, bool useNames = true, bool useDefaults = true)
        {
            StringBuilder sb = new();
            bool isFirst = true;

            for (int i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                var writeAttr = useAttributes && param.Attributes.Count > 0;
                var writeDefault = useDefaults && param.DefaultValue != null;

                if (!isFirst)
                    sb.Append(", ");

                sb.Append($"{(writeAttr ? string.Join(" ", param.Attributes) + " " : string.Empty)}{param.Type}{(useNames ? " " + param.Name : string.Empty)}{(writeDefault ? $" = {param.DefaultValue}" : string.Empty)}");
                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Executes public operation <c>BuildExtensionSignature</c>.
        /// </summary>
        public string BuildExtensionSignature(string type, string? name, bool useAttributes = true, bool useNames = true)
        {
            StringBuilder sb = new();
            sb.Append(useNames ? $"this {type} {name ?? string.Empty}" : $"this {type}");
            for (int i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                var writeAttr = useAttributes && param.Attributes.Count > 0;

                if (param.DefaultValue != null)
                    continue;

                sb.Append($", {(writeAttr ? string.Join(" ", param.Attributes) + " " : string.Empty)}{param.Type}{(useNames ? " " + param.Name : string.Empty)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Executes public operation <c>BuildGenericSignature</c>.
        /// </summary>
        public string BuildGenericSignature()
        {
            return string.Join(", ", GenericParameters.Select(p => p.Name));
        }

        /// <summary>
        /// Executes public operation <c>BuildGenericConstraint</c>.
        /// </summary>
        public string BuildGenericConstraint()
        {
            return string.Join(" ", GenericParameters.Select(p => p.Constrain));
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return BuildSignature();
        }

        /// <summary>
        /// Executes public operation <c>HasParameter</c>.
        /// </summary>
        public bool HasParameter(CsParameterInfo cppParameter)
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].Name == cppParameter.Name && Parameters[i].DefaultValue == cppParameter.DefaultValue)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetParameter</c>.
        /// </summary>
        public CsParameterInfo? GetParameter(string name)
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                var p = Parameters[i];
                if (p.Name == name)
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Attempts to resolve data via <c>TryGetParameter</c> without throwing.
        /// </summary>
        public bool TryGetParameter(string name, [NotNullWhen(true)] out CsParameterInfo? parameter)
        {
            parameter = GetParameter(name);
            return parameter != null;
        }

        /// <summary>
        /// Executes public operation <c>ShallowClone</c>.
        /// </summary>
        public CsFunctionVariation ShallowClone()
        {
            return new CsFunctionVariation(Identifier, ExportedName, Name, StructName, Kind, ReturnType.Clone());
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsFunctionVariation Clone()
        {
            return new CsFunctionVariation(Identifier, ExportedName, Name, StructName, Kind, ReturnType.Clone(), Parameters.CloneValues(), GenericParameters.CloneValues(), Modifiers.Clone(), Attributes.Clone());
        }

        /// <summary>
        /// Executes public operation <c>ValueVariation</c>.
        /// </summary>
        public static implicit operator ValueVariation(CsFunctionVariation variation)
        {
            return new ValueVariation(variation.Name, variation.Parameters);
        }
    }
}
