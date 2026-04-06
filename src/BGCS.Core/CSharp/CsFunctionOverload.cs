namespace BGCS.Core.CSharp
{
    using BGCS.Core.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines values for <c>CsFunctionKind</c>.
    /// </summary>
    public enum CsFunctionKind
    {
        Default,
        Constructor,
        Destructor,
        Operator,
        Member,
        Extension,
    }

    /// <summary>
    /// Defines the public class <c>CsFunctionOverload</c>.
    /// </summary>
    public class CsFunctionOverload : ICsFunction, ICloneable<CsFunctionOverload>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsFunctionOverload"/>.
        /// </summary>
        public CsFunctionOverload(string exportedName, string name, string? comment, Dictionary<string, string> defaultValues, string structName, CsFunctionKind kind, CsType returnType, List<CsParameterInfo> parameters, List<CsFunctionVariation> variations, List<string> modifiers, List<string> attributes)
        {
            ExportedName = exportedName;
            Name = name;
            Comment = comment;
            DefaultValues = defaultValues;
            StructName = structName;
            Kind = kind;
            ReturnType = returnType;
            Parameters = parameters;
            Variations = new(variations);
            Modifiers = modifiers;
            Attributes = attributes;
            for (int i = 0; i < variations.Count; i++)
            {
                ValueVariations.Add(variations[i]);
            }
        }

        /// <summary>
        /// Executes public operation <c>CsFunctionOverload</c>.
        /// </summary>
        public CsFunctionOverload(string exportedName, string name, string? comment, string structName, CsFunctionKind kind, CsType returnType)
        {
            ExportedName = exportedName;
            Name = name;
            Comment = comment;
            DefaultValues = new();
            StructName = structName;
            Kind = kind;
            ReturnType = returnType;
            Parameters = new();
            Variations = new();
            Modifiers = new();
            Attributes = new();
        }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets <c>DefaultValues</c>.
        /// </summary>
        public Dictionary<string, string> DefaultValues { get; }

        /// <summary>
        /// Gets or sets <c>StructName</c>.
        /// </summary>
        public string StructName { get; set; }

        /// <summary>
        /// Gets or sets <c>Kind</c>.
        /// </summary>
        public CsFunctionKind Kind { get; set; }

        /// <summary>
        /// Gets or sets <c>ReturnType</c>.
        /// </summary>
        public CsType ReturnType { get; set; }

        /// <summary>
        /// Gets or sets <c>Parameters</c>.
        /// </summary>
        public List<CsParameterInfo> Parameters { get; set; }

        /// <summary>
        /// Gets or sets <c>Variations</c>.
        /// </summary>
        public ConcurrentList<CsFunctionVariation> Variations { get; set; }

        [JsonIgnore]
        /// <summary>
        /// Gets or sets <c>ValueVariations</c>.
        /// </summary>
        public HashSet<ValueVariation> ValueVariations { get; set; } = [];

        /// <summary>
        /// Gets or sets <c>Modifiers</c>.
        /// </summary>
        public List<string> Modifiers { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string> Attributes { get; set; }

        /// <summary>
        /// Executes public operation <c>HasVariation</c>.
        /// </summary>
        public bool HasVariation(CsFunctionVariation variation)
        {
            lock (Variations.SyncObject)
            {
                return ValueVariations.Contains(variation);
            }
        }

        /// <summary>
        /// Executes public operation <c>HasVariation</c>.
        /// </summary>
        public bool HasVariation(ValueVariation variation)
        {
            lock (Variations.SyncObject)
            {
                return ValueVariations.Contains(variation);
            }
        }

        /// <summary>
        /// Executes public operation <c>TryAddVariation</c>.
        /// </summary>
        public bool TryAddVariation(CsFunctionVariation variation)
        {
            lock (Variations.SyncObject)
            {
                if (ValueVariations.Add(variation))
                {
                    Variations.Add(variation);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Executes public operation <c>TryAddVariation</c>.
        /// </summary>
        public bool TryAddVariation(ValueVariation valueVariation, [NotNullWhen(true)] out CsFunctionVariation? variation)
        {
            lock (Variations.SyncObject)
            {
                if (ValueVariations.Add(valueVariation))
                {
                    variation = CreateVariationWith();
                    variation.Parameters.AddRange(valueVariation.Parameters);
                    Variations.Add(variation);
                    return true;
                }
            }
            variation = null;
            return false;
        }

        /// <summary>
        /// Executes public operation <c>TryUpdateVariation</c>.
        /// </summary>
        public bool TryUpdateVariation(CsFunctionVariation oldVariation, CsFunctionVariation newVariation)
        {
            lock (Variations.SyncObject)
            {
                if (!HasVariation(newVariation))
                {
                    Variations.Add(newVariation);
                    Variations.Remove(oldVariation);

                    ValueVariations.Remove(oldVariation);
                    ValueVariations.Add(newVariation);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Executes public operation <c>BuildSignature</c>.
        /// </summary>
        public string BuildSignature()
        {
            return string.Join(", ", Parameters.Select(p => $"{string.Join(" ", p.Attributes)} {p.Type.Name} {p.Name}"));
        }

        /// <summary>
        /// Executes public operation <c>BuildSignatureNameless</c>.
        /// </summary>
        public string BuildSignatureNameless()
        {
            return string.Join(", ", Parameters.Select(p => $"{p.Name}"));
        }

        /// <summary>
        /// Executes public operation <c>BuildSignatureNamelessForCOM</c>.
        /// </summary>
        public string BuildSignatureNamelessForCOM(string comObject, IGeneratorConfig settings)
        {
            return $"{comObject}*{(Parameters.Count > 0 ? ", " : string.Empty)}{string.Join(", ", Parameters.Select(x => $"{(x.Type.IsBool ? settings.GetBoolType() : x.Type.Name)}"))}";
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
                if (Parameters[i].Name == cppParameter.Name)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Executes public operation <c>CreateVariationWith</c>.
        /// </summary>
        public CsFunctionVariation CreateVariationWith()
        {
            return new(null!, ExportedName, Name, StructName, Kind, ReturnType);
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsFunctionOverload Clone()
        {
            return new CsFunctionOverload(ExportedName, Name, Comment, DefaultValues.Clone(), StructName, Kind, ReturnType.Clone(), Parameters.CloneValues(), Variations.CloneValues(), Modifiers.Clone(), Attributes.Clone());
        }
    }
}
