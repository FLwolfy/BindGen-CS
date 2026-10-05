using System.Linq;
using BGCS.Core.Collections;

namespace BGCS.CSharp
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using Newtonsoft.Json;

    /// <summary>
    /// Retains mutable signature and marshalling information while managed overload projections are being analyzed.
    /// </summary>
    public class CsFunctionVariation : ICsFunction, ICloneable<CsFunctionVariation>, IHasIdentifier
    {
        /// <summary>
        /// Restores the supplied analysis descriptors and their collection ownership.
        /// </summary>
        /// <param name="identifier">
        /// The cached managed signature identity.
        /// </param>
        /// <param name="exportedName">
        /// The native entry-point symbol.
        /// </param>
        /// <param name="name">
        /// The managed method identifier.
        /// </param>
        /// <param name="structName">
        /// The managed receiver or containing structure name.
        /// </param>
        /// <param name="kind">
        /// The function projection category.
        /// </param>
        /// <param name="returnType">
        /// The mutable return-type descriptor retained by this model.
        /// </param>
        /// <param name="parameters">
        /// The ordered mutable parameter list retained without copying its descriptors.
        /// </param>
        /// <param name="genericParameters">
        /// The mutable generic declaration list retained by this model.
        /// </param>
        /// <param name="modifiers">
        /// The declaration modifier list retained by this model.
        /// </param>
        /// <param name="attributes">
        /// The managed attribute list retained by this model.
        /// </param>
        [JsonConstructor]
        public CsFunctionVariation(
            string identifier,
            string exportedName,
            string name,
            string structName,
            CsFunctionKind kind,
            CsType returnType,
            List<CsParameterInfo> parameters,
            List<CsGenericParameterInfo> genericParameters,
            List<string> modifiers,
            List<string> attributes
        ) {
            this.identifier = identifier;
            this.exportedName = exportedName;
            this.name = name;
            this.structName = structName;
            this.kind = kind;
            this.returnType = returnType;
            this.parameters = parameters;
            this.genericParameters = genericParameters;
            this.modifiers = modifiers;
            this.attributes = attributes;
        }

        /// <summary>
        /// Creates a function analysis model with empty parameter, modifier, and attribute collections.
        /// </summary>
        /// <param name="identifier">
        /// The cached managed signature identity.
        /// </param>
        /// <param name="exportedName">
        /// The native entry-point symbol.
        /// </param>
        /// <param name="name">
        /// The managed method identifier.
        /// </param>
        /// <param name="structName">
        /// The managed receiver or containing structure name.
        /// </param>
        /// <param name="kind">
        /// The function projection category.
        /// </param>
        /// <param name="returnType">
        /// The mutable return-type descriptor retained by this model.
        /// </param>
        public CsFunctionVariation(
            string identifier,
            string exportedName,
            string name,
            string structName,
            CsFunctionKind kind,
            CsType returnType
        ) {
            this.identifier = identifier;
            this.exportedName = exportedName;
            this.name = name;
            this.structName = structName;
            this.kind = kind;
            this.returnType = returnType;
            this.parameters = new();
            this.genericParameters = new();
            this.modifiers = new();
            this.attributes = new();
        }

        /// <summary>
        /// Gets or sets the signature identity cached by header-building operations.
        /// </summary>
        public string identifier { get; set; }
        /// <summary>
        /// Gets or sets the native entry-point symbol used by imports.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed method identifier used for emission.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the managed receiver or containing structure name.
        /// </summary>
        public string structName { get; set; }
        /// <summary>
        /// Gets or sets whether this projection represents a free function, member, constructor, destructor, operator, or extension.
        /// </summary>
        public CsFunctionKind kind { get; set; }
        /// <summary>
        /// Gets whether this variation declares any generic type parameters.
        /// </summary>
        public bool isGeneric => this.genericParameters.Count > 0;
        /// <summary>
        /// Gets or sets the mutable return-type descriptor retained during analysis.
        /// </summary>
        public CsType returnType { get; set; }
        /// <summary>
        /// Gets or sets the ordered mutable parameter descriptors retained during analysis.
        /// </summary>
        public List<CsParameterInfo> parameters { get; set; }
        /// <summary>
        /// Gets or sets the ordered generic declarations and their constraints.
        /// </summary>
        public List<CsGenericParameterInfo> genericParameters { get; set; }
        /// <summary>
        /// Gets or sets the managed declaration modifiers in emission order.
        /// </summary>
        public List<string> modifiers { get; set; }
        /// <summary>
        /// Gets or sets the managed attribute fragments in emission order.
        /// </summary>
        public List<string> attributes { get; set; }

        #region IDs
        /// <summary>
        /// Builds the selected variation signature for emission or conflict comparison.
        /// </summary>
        /// <param name="variation">Variation supplying parameter types and names.</param>
        /// <param name="useAttributes">Whether parameter attributes are included.</param>
        /// <param name="useNames">Whether parameter names are included.</param>
        /// <param name="conflictResolution">Whether identity comparison applies conflict normalization.</param>
        /// <param name="flags">Function emission context controlling receiver and argument projection.</param>
        /// <returns>Signature text for the selected context.</returns>
        protected virtual string BuildFunctionSignature(
            CsFunctionVariation variation,
            bool useAttributes,
            bool useNames,
            bool conflictResolution,
            WriteFunctionFlags flags
        ) {
            int offset = flags == WriteFunctionFlags.None ? 0 : 1;
            StringBuilder sb = new();
            bool isFirst = true;
            if (flags == WriteFunctionFlags.Extension)
            {
                isFirst = false;
                var first = variation.parameters[0];
                if (useNames)
                {
                    sb.Append($"this {first.type} {first.name}");
                }
                else
                {
                    sb.Append($"this {first.type}");
                }
            }

            for (int i = offset; i < variation.parameters.Count; i++)
            {
                var param = variation.parameters[i];
                if (param.defaultValue != null)
                    continue;
                if (!isFirst)
                    sb.Append(", ");
                if (useAttributes)
                {
                    sb.Append($"{string.Join(" ", param.attributes)} ");
                }

                if (conflictResolution && param.type.isRefOrIn)
                {
                    sb.Append("ref ");
                    sb.Append(param.type.GetNormalizedName());
                }
                else
                {
                    sb.Append($"{param.type}");
                }

                if (useNames)
                {
                    sb.Append($" {param.name}");
                }

                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats and stores a name-plus-parameter identity using normalized ref/in types and omitting defaulted parameters.
        /// </summary>
        /// <param name="flags">
        /// The receiver projection context; member and extension forms consume the first parameter as their receiver.
        /// </param>
        /// <returns>
        /// The formatted identity, also assigned to identifier.
        /// </returns>
        public string BuildFunctionHeaderId(WriteFunctionFlags flags)
        {
            string signature = BuildFunctionSignature(this, false, false, true, flags);
            return this.identifier = $"{this.name}({signature})";
        }

        /// <summary>
        /// Formats and stores a name-plus-parameter identity using normalized ref/in types and omitting defaulted parameters.
        /// </summary>
        /// <param name="alias">
        /// The alternate managed method identifier.
        /// </param>
        /// <param name="flags">
        /// The receiver projection context; member and extension forms consume the first parameter as their receiver.
        /// </param>
        /// <returns>
        /// The formatted identity, also assigned to identifier.
        /// </returns>
        public string BuildFunctionHeaderId(
            string alias,
            WriteFunctionFlags flags
        ) {
            string signature = BuildFunctionSignature(this, false, false, true, flags);
            return this.identifier = $"{alias}({signature})";
        }

        /// <summary>
        /// Formats and stores a complete method header, including generic constraints when present.
        /// </summary>
        /// <param name="csReturnType">
        /// The return type to emit.
        /// </param>
        /// <param name="flags">
        /// The receiver projection context; member and extension forms consume the first parameter as their receiver.
        /// </param>
        /// <param name="generateMetadata">
        /// Whether to include managed parameter attributes.
        /// </param>
        /// <returns>
        /// The complete header text, also assigned to identifier.
        /// </returns>
        public string BuildFunctionHeader(
            CsType csReturnType,
            WriteFunctionFlags flags,
            bool generateMetadata
        ) {
            string signature = BuildFunctionSignature(this, generateMetadata, true, false, flags);
            if (this.isGeneric)
            {
                return this.identifier = $"{csReturnType.name} {this.name}<{BuildGenericSignature()}>({signature}) {BuildGenericConstraint()}";
            }
            else
            {
                return this.identifier = $"{csReturnType.name} {this.name}({signature})";
            }
        }

        /// <summary>
        /// Formats and stores a complete method header, including generic constraints when present.
        /// </summary>
        /// <param name="alias">
        /// The alternate managed method identifier.
        /// </param>
        /// <param name="csReturnType">
        /// The return type to emit.
        /// </param>
        /// <param name="flags">
        /// The receiver projection context; member and extension forms consume the first parameter as their receiver.
        /// </param>
        /// <param name="generateMetadata">
        /// Whether to include managed parameter attributes.
        /// </param>
        /// <returns>
        /// The complete header text, also assigned to identifier.
        /// </returns>
        public string BuildFunctionHeader(
            string alias,
            CsType csReturnType,
            WriteFunctionFlags flags,
            bool generateMetadata
        ) {
            string signature = BuildFunctionSignature(this, generateMetadata, true, false, flags);
            if (this.isGeneric)
            {
                return this.identifier = $"{csReturnType.name} {alias}<{BuildGenericSignature()}>({signature}) {BuildGenericConstraint()}";
            }
            else
            {
                return this.identifier = $"{csReturnType.name} {alias}({signature})";
            }
        }

        /// <summary>
        /// Formats an invocation of this managed variation, forwarding non-defaulted arguments and ref/out modifiers.
        /// </summary>
        /// <param name="flags">
        /// The receiver projection context; member and extension forms consume the first parameter as their receiver.
        /// </param>
        /// <returns>
        /// The invocation text with generic arguments when present.
        /// </returns>
        public string BuildFunctionOverload(WriteFunctionFlags flags)
        {
            string signature = BuildFunctionOverload(this, flags);
            if (this.isGeneric)
            {
                return $"{this.name}<{BuildGenericSignature()}>({signature})";
            }
            else
            {
                return $"{this.name}({signature})";
            }
        }

        /// <summary>
        /// Builds the invocation arguments forwarding this variation to its native overload.
        /// </summary>
        /// <param name="variation">Variation supplying parameter and receiver information.</param>
        /// <param name="flags">Invocation context controlling receiver projection.</param>
        /// <returns>Comma-separated forwarding arguments.</returns>
        protected virtual string BuildFunctionOverload(
            CsFunctionVariation variation,
            WriteFunctionFlags flags
        ) {
            int offset = flags == WriteFunctionFlags.None ? 0 : 1;
            StringBuilder sb = new();
            bool isFirst = true;
            if (flags == WriteFunctionFlags.Extension)
            {
                isFirst = false;
                var first = variation.parameters[0];
                sb.Append("this");
                sb.Append($" {first.name}");
            }

            for (int i = offset; i < variation.parameters.Count; i++)
            {
                bool written = false;
                var param = variation.parameters[i];
                if (param.defaultValue != null)
                    continue;
                if (!isFirst)
                    sb.Append(", ");
                if (param.type.isRef)
                {
                    sb.Append("ref");
                    written = true;
                }

                if (param.type.isOut)
                {
                    sb.Append("out");
                    written = true;
                }

                if (written)
                    sb.Append(' ');
                sb.Append(param.name);
                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats and stores a constructor identity without parameter names, attributes, or defaults.
        /// </summary>
        /// <returns>
        /// The constructor identity, also assigned to identifier.
        /// </returns>
        public string BuildConstructorSignatureIdentifier()
        {
            return this.identifier = $"{this.structName}({BuildConstructorSignature(false, false, false)})";
        }

        #endregion IDs
        /// <summary>
        /// Formats a constructor declaration with parameter names and available default expressions.
        /// </summary>
        /// <param name="generateMetadata">
        /// Whether to include parameter attributes.
        /// </param>
        /// <returns>
        /// The containing structure name and complete constructor parameter list.
        /// </returns>
        public string BuildFullConstructorSignature(bool generateMetadata)
        {
            return $"{this.structName}({BuildConstructorSignature(generateMetadata)})";
        }

        /// <summary>
        /// Formats the complete method declaration with generic arguments and constraints.
        /// </summary>
        /// <returns>
        /// The return type, method name, non-defaulted parameter declarations, and generic constraints.
        /// </returns>
        public string BuildFullSignature()
        {
            return $"{this.returnType.name} {this.name}{(this.isGeneric ? $"<{BuildGenericSignature()}>" : string.Empty)}({BuildSignature()}) {BuildGenericConstraint()}";
        }

        /// <summary>
        /// Formats a complete extension-method declaration using an explicit receiver.
        /// </summary>
        /// <param name="type">
        /// The receiver's managed type spelling.
        /// </param>
        /// <param name="name">
        /// The receiver's managed parameter name.
        /// </param>
        /// <returns>
        /// The complete extension declaration, including generic constraints.
        /// </returns>
        public string BuildFullExtensionSignature(
            string type,
            string name
        ) {
            return $"{this.returnType.name} {this.name}{(this.isGeneric ? $"<{BuildGenericSignature()}>" : string.Empty)}({BuildExtensionSignature(type, name)}) {BuildGenericConstraint()}";
        }

        /// <summary>
        /// Formats ordered method parameters, omitting those with default-value expressions.
        /// </summary>
        /// <param name="useAttributes">
        /// Whether to include managed parameter attribute fragments.
        /// </param>
        /// <param name="useNames">
        /// Whether to include parameter identifiers.
        /// </param>
        /// <returns>
        /// The comma-separated parameter declarations, or an empty string when none remain.
        /// </returns>
        public string BuildSignature(
            bool useAttributes = true,
            bool useNames = true
        ) {
            StringBuilder sb = new();
            bool isFirst = true;
            for (int i = 0; i < this.parameters.Count; i++)
            {
                var param = this.parameters[i];
                var writeAttr = useAttributes && param.attributes.Count > 0;
                if (param.defaultValue != null)
                    continue;
                if (!isFirst)
                    sb.Append(", ");
                sb.Append($"{(writeAttr ? string.Join(" ", param.attributes) + " " : string.Empty)}{param.type}{(useNames ? " " + param.name : string.Empty)}");
                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats all constructor parameters, optionally including attributes, names, and default expressions.
        /// </summary>
        /// <param name="useAttributes">
        /// Whether to include managed parameter attribute fragments.
        /// </param>
        /// <param name="useNames">
        /// Whether to include parameter identifiers.
        /// </param>
        /// <param name="useDefaults">
        /// Whether to append available default-value expressions.
        /// </param>
        /// <returns>
        /// The comma-separated constructor parameter declarations.
        /// </returns>
        public string BuildConstructorSignature(
            bool useAttributes = true,
            bool useNames = true,
            bool useDefaults = true
        ) {
            StringBuilder sb = new();
            bool isFirst = true;
            for (int i = 0; i < this.parameters.Count; i++)
            {
                var param = this.parameters[i];
                var writeAttr = useAttributes && param.attributes.Count > 0;
                var writeDefault = useDefaults && param.defaultValue != null;
                if (!isFirst)
                    sb.Append(", ");
                sb.Append($"{(writeAttr ? string.Join(" ", param.attributes) + " " : string.Empty)}{param.type}{(useNames ? " " + param.name : string.Empty)}{(writeDefault ? $" = {param.defaultValue}" : string.Empty)}");
                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Prepends an explicit extension receiver to the non-defaulted parameter declarations.
        /// </summary>
        /// <param name="type">
        /// The receiver's managed type spelling.
        /// </param>
        /// <param name="name">
        /// The receiver identifier, or null for an unnamed receiver.
        /// </param>
        /// <param name="useAttributes">
        /// Whether to include managed parameter attribute fragments.
        /// </param>
        /// <param name="useNames">
        /// Whether to include parameter identifiers.
        /// </param>
        /// <returns>
        /// The receiver and comma-separated parameter declarations.
        /// </returns>
        public string BuildExtensionSignature(
            string type,
            string? name,
            bool useAttributes = true,
            bool useNames = true
        ) {
            StringBuilder sb = new();
            sb.Append(useNames ? $"this {type} {name ?? string.Empty}" : $"this {type}");
            for (int i = 0; i < this.parameters.Count; i++)
            {
                var param = this.parameters[i];
                var writeAttr = useAttributes && param.attributes.Count > 0;
                if (param.defaultValue != null)
                    continue;
                sb.Append($", {(writeAttr ? string.Join(" ", param.attributes) + " " : string.Empty)}{param.type}{(useNames ? " " + param.name : string.Empty)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Joins generic parameter identifiers in declaration order.
        /// </summary>
        /// <returns>
        /// The comma-separated generic parameter names, or an empty string for a non-generic method.
        /// </returns>
        public string BuildGenericSignature()
        {
            return string.Join(", ", this.genericParameters.Select(p => p.name));
        }

        /// <summary>
        /// Joins generic constraint fragments in declaration order.
        /// </summary>
        /// <returns>
        /// The space-separated constraints, or an empty string when none exist.
        /// </returns>
        public string BuildGenericConstraint()
        {
            return string.Join(" ", this.genericParameters.Select(p => p.constrain));
        }

        /// <summary>
        /// Formats the current ordered parameter declarations for diagnostic display.
        /// </summary>
        /// <returns>
        /// The comma-separated parameter signature, or an empty string when no parameters exist.
        /// </returns>
        public override string ToString()
        {
            return BuildSignature();
        }

        /// <summary>
        /// Searches for a parameter with matching managed name and default expression.
        /// </summary>
        /// <param name="cppParameter">
        /// The parameter descriptor to compare; its native AST identity is not compared.
        /// </param>
        /// <returns>
        /// True when an equivalent name/default pair exists; otherwise false.
        /// </returns>
        public bool HasParameter(CsParameterInfo cppParameter)
        {
            for (int i = 0; i < this.parameters.Count; i++)
            {
                if (this.parameters[i].name == cppParameter.name && this.parameters[i].defaultValue == cppParameter.defaultValue)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Finds the first parameter whose managed name exactly matches the supplied name.
        /// </summary>
        /// <param name="name">
        /// The case-sensitive managed parameter name.
        /// </param>
        /// <returns>
        /// The retained mutable descriptor, or null when no parameter matches.
        /// </returns>
        public CsParameterInfo? GetParameter(string name)
        {
            for (int i = 0; i < this.parameters.Count; i++)
            {
                var p = this.parameters[i];
                if (p.name == name)
                    return p;
            }

            return null;
        }

        /// <summary>
        /// Finds a retained parameter by its exact managed name.
        /// </summary>
        /// <param name="name">
        /// The case-sensitive managed parameter name.
        /// </param>
        /// <param name="parameter">
        /// The matching descriptor, or null when absent.
        /// </param>
        /// <returns>
        /// True when a parameter matches; otherwise false.
        /// </returns>
        public bool TryGetParameter(
            string name,
            [NotNullWhen(true)] out CsParameterInfo? parameter
        ) {
            parameter = GetParameter(name);
            return parameter != null;
        }

        /// <summary>
        /// Copies the function identity and return type into a variation with empty parameter and declaration collections.
        /// </summary>
        /// <returns>
        /// A new variation with a cloned return type and no parameters, generic parameters, modifiers, or attributes.
        /// </returns>
        public CsFunctionVariation ShallowClone()
        {
            return new CsFunctionVariation(this.identifier, this.exportedName, this.name, this.structName, this.kind, this.returnType.Clone());
        }

        /// <summary>
        /// Clones mutable managed descriptors and collection containers while retaining native AST references owned by the analysis invocation.
        /// </summary>
        /// <returns>
        /// A separately mutable function model; native source declarations are shared, not copied.
        /// </returns>
        public CsFunctionVariation Clone()
        {
            return new CsFunctionVariation(this.identifier, this.exportedName, this.name, this.structName, this.kind, this.returnType.Clone(), this.parameters.CloneValues(), this.genericParameters.CloneValues(), this.modifiers.Clone(), this.attributes.Clone());
        }

        /// <summary>
        /// Projects this variation into the signature key used for overload deduplication.
        /// </summary>
        /// <param name="variation">
        /// The variation supplying its managed name and ordered parameter descriptors.
        /// </param>
        /// <returns>
        /// The signature comparison value.
        /// </returns>
        public static implicit operator ValueVariation(CsFunctionVariation variation)
        {
            return new ValueVariation(variation.name, variation.parameters);
        }
    }
}
