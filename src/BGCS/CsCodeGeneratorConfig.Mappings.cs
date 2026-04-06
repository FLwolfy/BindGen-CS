namespace BGCS
{
    using BGCS.Core.Mapping;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>CsCodeGeneratorConfig</c>.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        /// <summary>
        /// Allows to inject data and modify constants. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<ConstantMapping> ConstantMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify enums. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<EnumMapping> EnumMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify functions. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<FunctionMapping> FunctionMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify handles. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<HandleMapping> HandleMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify classes. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<TypeMapping> ClassMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify delegates. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<DelegateMapping> DelegateMappings { get; set; } = null!;

        /// <summary>
        /// Allows to inject data and modify arrays. (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public List<ArrayMapping> ArrayMappings { get; set; } = null!;

        /// <summary>
        /// Allows to modify names fully or partially. newName = newName.Replace(item.Key, item.Value, StringComparison.InvariantCultureIgnoreCase); (Default: Empty)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> NameMappings { get; set; } = null!;

        /// <summary>
        /// Maps type Key to type Value. (Default: a list with common types, like size_t : nuint)
        /// </summary>
        [DefaultValue(null)]
        public Dictionary<string, string> TypeMappings { get; set; } = null!;

        /// <summary>
        /// Gets or sets the mappings from typedef names to corresponding enum names.
        /// </summary>
        /// <remarks>Use this property to specify how typedefs should be mapped to enums during code
        /// generation or processing. Each key represents a typedef name, and its value specifies the enum name to use
        /// in place of the typedef.</remarks>
        [DefaultValue(null)]
        public Dictionary<string, string?> TypedefToEnumMappings { get; set; } = null!;

        [DefaultValue(null)]
        /// <summary>
        /// Gets or sets <c>FunctionAliasMappings</c>.
        /// </summary>
        public Dictionary<string, List<FunctionAliasMapping>> FunctionAliasMappings { get; set; } = null!;

        #region FunctionAlias

        /// <summary>
        /// Attempts to resolve data via <c>TryGetFunctionAliasMapping</c> without throwing.
        /// </summary>
        public bool TryGetFunctionAliasMapping(string name, string aliasName, [NotNullWhen(true)] out FunctionAliasMapping? mapping)
        {
            if (!FunctionAliasMappings.TryGetValue(name, out var aliases))
            {
                mapping = null;
                return false;
            }

            foreach (var aliasMapping in aliases)
            {
                if (aliasMapping.ExportedAliasName == aliasName)
                {
                    mapping = aliasMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetFunctionAliasMapping</c>.
        /// </summary>
        public FunctionAliasMapping? GetFunctionAliasMapping(string name, string aliasName)
        {
            if (!FunctionAliasMappings.TryGetValue(name, out var aliases))
            {
                return null;
            }

            foreach (var aliasMapping in aliases)
            {
                if (aliasMapping.ExportedAliasName == aliasName)
                {
                    return aliasMapping;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds data or behavior through <c>AddFunctionAliasMapping</c>.
        /// </summary>
        public List<FunctionAliasMapping> AddFunctionAliasMapping(FunctionAliasMapping alias)
        {
            if (!FunctionAliasMappings.TryGetValue(alias.ExportedName, out var aliases))
            {
                aliases = [];
                FunctionAliasMappings.Add(alias.ExportedName, aliases);
            }

            aliases.Add(alias);
            return aliases;
        }

        #endregion FunctionAlias
    }
}
