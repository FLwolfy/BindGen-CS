namespace BGCS.Configuration
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using BGCS.Configuration.Mapping;

    /// <summary>
    /// Stores explicit declaration, marshalling, external-layout, and alias mapping policies for a binding run.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        /// <summary>
        /// Allows to inject data and modify constants. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<ConstantMapping> constantMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify enums. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<EnumMapping> enumMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify functions. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<FunctionMapping> functionMappings { get; set; } = null!;
        /// <summary>
        /// Maps native C variadic functions to explicit promoted fixed signatures keyed by native function name.
        /// </summary>
        public Dictionary<string, List<VariadicFunctionVariant>> variadicFunctionVariants { get; set; } = null!;
        /// <summary>
        /// Overrides inferred ownership, encoding, buffer relationships, and cleanup semantics by native function name.
        /// </summary>
        public Dictionary<string, FunctionMarshallingMapping> marshallingMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify handles. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<HandleMapping> handleMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify classes. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<TypeMapping> classMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify delegates. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<DelegateMapping> delegateMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify arrays. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<ArrayMapping> arrayMappings { get; set; } = null!;

        /// <summary>
        /// Allows to modify names fully or partially. newName = newName.Replace(item.Key, item.Value, StringComparison.InvariantCultureIgnoreCase); (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> nameMappings { get; set; } = null!;

        /// <summary>
        /// Maps type Key to type Value. (Default: a list with common types, like size_t : nuint)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> typeMappings { get; set; } = null!;

        /// <summary>
        /// Declares target-specific ABI evidence for managed value types supplied by the consuming project.
        /// </summary>
        /// <remarks>
        /// A contract does not generate the managed carrier. Pair every native name with a matching
        /// <see cref = "typeMappings"/> entry and make the carrier available through <see cref = "CsCodeGeneratorConfig.usings"/>.
        /// </remarks>
        [DefaultValue(null)]
        public List<ExternalTypeContract> externalTypeContracts { get; set; } = null!;

        /// <summary>
        /// Gets or sets the mappings from typedef names to corresponding enum names.
        /// </summary>
        /// <remarks>Use this property to specify how typedefs should be mapped to enums during code
        /// generation or processing. Each key represents a typedef name, and its value specifies the enum name to use
        /// in place of the typedef.</remarks>
        [DefaultValue(null)]
        public Dictionary<string, string?> typedefToEnumMappings { get; set; } = null!;

        /// <summary>
        /// Gets or sets mutable alias mappings grouped by their original native function identifier.
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, List<FunctionAliasMapping>> functionAliasMappings { get; set; } = null!;

        #region FunctionAlias
        /// <summary>
        /// Finds the first alias mapping under an exact native function and alias identifier.
        /// </summary>
        /// <param name="name">
        /// The original native function identifier.
        /// </param>
        /// <param name="aliasName">
        /// The exported alias identifier to match.
        /// </param>
        /// <param name="mapping">
        /// Receives the retained mutable alias mapping, or null when no match exists.
        /// </param>
        /// <returns>
        /// True when the original function group contains the requested alias.
        /// </returns>
        public bool TryGetFunctionAliasMapping(
            string name,
            string aliasName,
            [NotNullWhen(true)] out FunctionAliasMapping? mapping
        ) {
            if (!this.functionAliasMappings.TryGetValue(name, out var aliases))
            {
                mapping = null;
                return false;
            }

            foreach (var aliasMapping in aliases)
            {
                if (aliasMapping.exportedAliasName == aliasName)
                {
                    mapping = aliasMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first alias mapping under an exact native function and alias identifier.
        /// </summary>
        /// <param name="name">
        /// The original native function identifier.
        /// </param>
        /// <param name="aliasName">
        /// The exported alias identifier to match.
        /// </param>
        /// <returns>
        /// The retained mutable alias mapping, or null when the function or alias is absent.
        /// </returns>
        public FunctionAliasMapping? GetFunctionAliasMapping(
            string name,
            string aliasName
        ) {
            if (!this.functionAliasMappings.TryGetValue(name, out var aliases))
            {
                return null;
            }

            foreach (var aliasMapping in aliases)
            {
                if (aliasMapping.exportedAliasName == aliasName)
                {
                    return aliasMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Appends an alias mapping to the group identified by its original native function name.
        /// </summary>
        /// <param name="alias">
        /// The mutable alias descriptor retained without copying; duplicate alias names are not removed.
        /// </param>
        /// <returns>
        /// The configuration-owned mutable alias list after appending the descriptor.
        /// </returns>
        public List<FunctionAliasMapping> AddFunctionAliasMapping(FunctionAliasMapping alias)
        {
            if (!this.functionAliasMappings.TryGetValue(alias.exportedName, out var aliases))
            {
                aliases = [];
                this.functionAliasMappings.Add(alias.exportedName, aliases);
            }

            aliases.Add(alias);
            return aliases;
        }
        #endregion FunctionAlias
    }
}
