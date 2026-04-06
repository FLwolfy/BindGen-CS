namespace BGCS
{
    using BGCS.Core.CSharp;
    using BGCS.CppAst.Model.Metadata;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>ParseResult</c>.
    /// </summary>
    public class ParseResult
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ParseResult"/>.
        /// </summary>
        public ParseResult(CppCompilation compilation)
        {
            Compilation = compilation;
        }

        /// <summary>
        /// Gets or sets <c>Compilation</c>.
        /// </summary>
        public CppCompilation Compilation { get; set; }

        /// <summary>
        /// Gets or sets <c>FunctionAliases</c>.
        /// </summary>
        public Dictionary<string, List<FunctionAlias>> FunctionAliases { get; set; } = [];

        /// <summary>
        /// Adds data or behavior through <c>AddFunctionAlias</c>.
        /// </summary>
        public List<FunctionAlias> AddFunctionAlias(FunctionAlias alias)
        {
            if (!FunctionAliases.TryGetValue(alias.ExportedName, out var aliases))
            {
                aliases = [];
                FunctionAliases.Add(alias.ExportedName, aliases);
            }

            aliases.Add(alias);
            return aliases;
        }

        /// <summary>
        /// Executes public operation <c>EnumerateFunctionAliases</c>.
        /// </summary>
        public IEnumerable<FunctionAlias> EnumerateFunctionAliases(string name)
        {
            if (!FunctionAliases.TryGetValue(name, out var aliases))
            {
                yield break;
            }

            foreach (var alias in aliases)
            {
                yield return alias;
            }
        }
    }
}
