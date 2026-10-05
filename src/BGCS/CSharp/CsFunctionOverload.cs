using System.Linq;
using BGCS.Configuration;

namespace BGCS.CSharp
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using BGCS.Core.Collections;
    using Newtonsoft.Json;

    /// <summary>
    /// Selects the native operation projected by a managed function analysis model.
    /// </summary>
    public enum CsFunctionKind
    {
        /// <summary>
        /// Ordinary free function import.
        /// </summary>
        Default,
        /// <summary>
        /// Native object construction.
        /// </summary>
        Constructor,
        /// <summary>
        /// Native object destruction.
        /// </summary>
        Destructor,
        /// <summary>
        /// Native operator projection.
        /// </summary>
        Operator,
        /// <summary>
        /// Native instance member projection.
        /// </summary>
        Member,
        /// <summary>
        /// Managed extension projection over a native receiver.
        /// </summary>
        Extension,
    }

    /// <summary>
    /// Retains mutable signature and marshalling information while managed overload projections are being analyzed.
    /// </summary>
    public class CsFunctionOverload : ICsFunction, ICloneable<CsFunctionOverload>
    {
        /// <summary>
        /// Restores the supplied analysis descriptors and their collection ownership.
        /// </summary>
        /// <param name="exportedName">
        /// The native entry-point symbol.
        /// </param>
        /// <param name="name">
        /// The managed method identifier.
        /// </param>
        /// <param name="comment">
        /// The documentation fragment, or null when absent.
        /// </param>
        /// <param name="defaultValues">
        /// The mutable default-expression map retained by this model.
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
        /// <param name="variations">
        /// Initial variations copied into an owned synchronized container; their descriptors are retained.
        /// </param>
        /// <param name="modifiers">
        /// The declaration modifier list retained by this model.
        /// </param>
        /// <param name="attributes">
        /// The managed attribute list retained by this model.
        /// </param>
        [JsonConstructor]
        public CsFunctionOverload(
            string exportedName,
            string name,
            string? comment,
            Dictionary<string, string> defaultValues,
            string structName,
            CsFunctionKind kind,
            CsType returnType,
            List<CsParameterInfo> parameters,
            List<CsFunctionVariation> variations,
            List<string> modifiers,
            List<string> attributes
        ) {
            this.exportedName = exportedName;
            this.name = name;
            this.comment = comment;
            this.defaultValues = defaultValues;
            this.structName = structName;
            this.kind = kind;
            this.returnType = returnType;
            this.parameters = parameters;
            this.variations = new(variations);
            this.modifiers = modifiers;
            this.attributes = attributes;
            for (int i = 0; i < variations.Count; i++)
            {
                this.valueVariations.Add(variations[i]);
            }
        }

        /// <summary>
        /// Creates a function analysis model with empty parameter, modifier, and attribute collections.
        /// </summary>
        /// <param name="exportedName">
        /// The native entry-point symbol.
        /// </param>
        /// <param name="name">
        /// The managed method identifier.
        /// </param>
        /// <param name="comment">
        /// The documentation fragment, or null when absent.
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
        public CsFunctionOverload(
            string exportedName,
            string name,
            string? comment,
            string structName,
            CsFunctionKind kind,
            CsType returnType
        ) {
            this.exportedName = exportedName;
            this.name = name;
            this.comment = comment;
            this.defaultValues = new();
            this.structName = structName;
            this.kind = kind;
            this.returnType = returnType;
            this.parameters = new();
            this.variations = new();
            this.modifiers = new();
            this.attributes = new();
        }

        /// <summary>
        /// Gets or sets the native entry-point symbol used by imports.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed method identifier used for emission.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the generated documentation fragment, or null when none was supplied.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets the mutable parameter-name to default-expression map.
        /// </summary>
        public Dictionary<string, string> defaultValues { get; }
        /// <summary>
        /// Gets or sets the managed receiver or containing structure name.
        /// </summary>
        public string structName { get; set; }
        /// <summary>
        /// Gets or sets whether this projection represents a free function, member, constructor, destructor, operator, or extension.
        /// </summary>
        public CsFunctionKind kind { get; set; }
        /// <summary>
        /// Gets or sets the mutable return-type descriptor retained during analysis.
        /// </summary>
        public CsType returnType { get; set; }
        /// <summary>
        /// Gets or sets the ordered mutable parameter descriptors retained during analysis.
        /// </summary>
        public List<CsParameterInfo> parameters { get; set; }
        /// <summary>
        /// Gets or sets the synchronized collection of accepted managed projections.
        /// </summary>
        public ConcurrentList<CsFunctionVariation> variations { get; set; }

        /// <summary>
        /// Gets or sets the signature comparison set; compound updates synchronize on variations.syncObject.
        /// </summary>
        [JsonIgnore]
        public HashSet<ValueVariation> valueVariations { get; set; } = [];
        /// <summary>
        /// Gets or sets the managed declaration modifiers in emission order.
        /// </summary>
        public List<string> modifiers { get; set; }
        /// <summary>
        /// Gets or sets the managed attribute fragments in emission order.
        /// </summary>
        public List<string> attributes { get; set; }

        /// <summary>
        /// Tests whether the synchronized signature comparison set already contains an equivalent projection.
        /// </summary>
        /// <param name="variation">
        /// The signature projection to compare.
        /// </param>
        /// <returns>
        /// True for an existing equivalent signature; otherwise false.
        /// </returns>
        public bool HasVariation(CsFunctionVariation variation)
        {
            lock (this.variations.syncObject)
            {
                return this.valueVariations.Contains(variation);
            }
        }

        /// <summary>
        /// Tests whether the synchronized signature comparison set already contains an equivalent projection.
        /// </summary>
        /// <param name="variation">
        /// The signature projection to compare.
        /// </param>
        /// <returns>
        /// True for an existing equivalent signature; otherwise false.
        /// </returns>
        public bool HasVariation(ValueVariation variation)
        {
            lock (this.variations.syncObject)
            {
                return this.valueVariations.Contains(variation);
            }
        }

        /// <summary>
        /// Adds a variation and its signature key under the collection's shared monitor when the signature is new.
        /// </summary>
        /// <param name="variation">
        /// The mutable variation to retain on success.
        /// </param>
        /// <returns>
        /// True when both collections are updated; false when an equivalent signature already exists.
        /// </returns>
        public bool TryAddVariation(CsFunctionVariation variation)
        {
            lock (this.variations.syncObject)
            {
                if (this.valueVariations.Add(variation))
                {
                    this.variations.Add(variation);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates and retains a variation from a new signature key under the collection's shared monitor.
        /// </summary>
        /// <param name="valueVariation">
        /// The managed name and parameters to project.
        /// </param>
        /// <param name="variation">
        /// The newly retained variation on success; null for a duplicate signature.
        /// </param>
        /// <returns>
        /// True when a new variation is created; otherwise false.
        /// </returns>
        public bool TryAddVariation(
            ValueVariation valueVariation,
            [NotNullWhen(true)] out CsFunctionVariation? variation
        ) {
            lock (this.variations.syncObject)
            {
                if (this.valueVariations.Add(valueVariation))
                {
                    variation = CreateVariationWith();
                    variation.parameters.AddRange(valueVariation.parameters);
                    this.variations.Add(variation);
                    return true;
                }
            }

            variation = null;
            return false;
        }

        /// <summary>
        /// Replaces a retained variation and its comparison key when the replacement signature is not already present.
        /// </summary>
        /// <param name="oldVariation">
        /// The existing variation to replace.
        /// </param>
        /// <param name="newVariation">
        /// The replacement variation to retain.
        /// </param>
        /// <returns>
        /// True when the replacement is applied; false when the old variation is absent or the replacement signature conflicts.
        /// </returns>
        public bool TryUpdateVariation(
            CsFunctionVariation oldVariation,
            CsFunctionVariation newVariation
        ) {
            lock (this.variations.syncObject)
            {
                if (!this.variations.Contains(oldVariation))
                    return false;
                if (!HasVariation(newVariation))
                {
                    this.variations.Add(newVariation);
                    this.variations.Remove(oldVariation);
                    this.valueVariations.Remove(oldVariation);
                    this.valueVariations.Add(newVariation);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Formats the current parameter attributes, managed types, and names in declaration order.
        /// </summary>
        /// <returns>
        /// The comma-separated parameter declarations, or an empty string when no parameters exist.
        /// </returns>
        public string BuildSignature()
        {
            return string.Join(", ", this.parameters.Select(p => $"{string.Join(" ", p.attributes)} {p.type.name} {p.name}"));
        }

        /// <summary>
        /// Formats the current managed parameter identifiers for forwarding an invocation.
        /// </summary>
        /// <returns>
        /// The comma-separated parameter names, or an empty string when no parameters exist.
        /// </returns>
        public string BuildSignatureNameless()
        {
            return string.Join(", ", this.parameters.Select(p => $"{p.name}"));
        }

        /// <summary>
        /// Formats a COM function-pointer parameter list beginning with the native receiver and using the configured Boolean carrier.
        /// </summary>
        /// <param name="comObject">
        /// The native receiver type spelling.
        /// </param>
        /// <param name="settings">
        /// The configuration supplying the native Boolean carrier spelling.
        /// </param>
        /// <returns>
        /// The receiver pointer followed by comma-separated native parameter types.
        /// </returns>
        public string BuildSignatureNamelessForCOM(
            string comObject,
            IGeneratorConfig settings
        ) {
            return $"{comObject}*{(this.parameters.Count > 0 ? ", " : string.Empty)}{string.Join(", ", this.parameters.Select(x => $"{(x.type.isBool ? settings.GetBoolType() : x.type.name)}"))}";
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
        /// Searches for a parameter with the same managed name.
        /// </summary>
        /// <param name="cppParameter">
        /// The parameter descriptor whose name is compared.
        /// </param>
        /// <returns>
        /// True when the managed name is present; otherwise false.
        /// </returns>
        public bool HasParameter(CsParameterInfo cppParameter)
        {
            for (int i = 0; i < this.parameters.Count; i++)
            {
                if (this.parameters[i].name == cppParameter.name)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates an empty managed variation that shares this overload's return-type descriptor and function identity.
        /// </summary>
        /// <returns>
        /// A new variation with empty declaration collections; identifier is populated later by header construction.
        /// </returns>
        public CsFunctionVariation CreateVariationWith()
        {
            return new(null!, this.exportedName, this.name, this.structName, this.kind, this.returnType);
        }

        /// <summary>
        /// Clones mutable managed descriptors and collection containers while retaining native AST references owned by the analysis invocation.
        /// </summary>
        /// <returns>
        /// A separately mutable function model; native source declarations are shared, not copied.
        /// </returns>
        public CsFunctionOverload Clone()
        {
            return new CsFunctionOverload(this.exportedName, this.name, this.comment, this.defaultValues.Clone(), this.structName, this.kind, this.returnType.Clone(), this.parameters.CloneValues(), this.variations.CloneValues(), this.modifiers.Clone(), this.attributes.Clone());
        }
    }
}
