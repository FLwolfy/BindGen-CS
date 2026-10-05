namespace BGCS.Analysis
{
    using System.Collections.Generic;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CSharp;

    /// <summary>
    /// Owns attempt-local mutable preprocessing projections while borrowing the native compilation lifetime.
    /// </summary>
    public class ParseResult
    {
        /// <summary>
        /// Captures the compilation analyzed by this generation attempt.
        /// </summary>
        /// <param name="compilation">
        /// The borrowed compilation; it must remain alive until preprocessing and analysis finish.
        /// </param>
        public ParseResult(CppCompilation compilation)
        {
            this.compilation = compilation;
        }

        /// <summary>
        /// Gets or sets the borrowed native compilation analyzed by this attempt.
        /// </summary>
        public CppCompilation compilation { get; set; }
        /// <summary>
        /// Gets or sets mutable discovered aliases grouped by the original native export name.
        /// </summary>
        public Dictionary<string, List<FunctionAlias>> functionAliases { get; set; } = [];

        /// <summary>
        /// Appends a discovered alias without copying its descriptor or deduplicating existing names.
        /// </summary>
        /// <param name="alias">
        /// The mutable attempt-local descriptor retained under its original export name.
        /// </param>
        /// <returns>
        /// The attempt-owned mutable alias group after appending the descriptor.
        /// </returns>
        public List<FunctionAlias> AddFunctionAlias(FunctionAlias alias)
        {
            if (!this.functionAliases.TryGetValue(alias.exportedName, out var aliases))
            {
                aliases = [];
                this.functionAliases.Add(alias.exportedName, aliases);
            }

            aliases.Add(alias);
            return aliases;
        }

        /// <summary>
        /// Enumerates aliases discovered for one exact native export name.
        /// </summary>
        /// <param name="name">
        /// The original native function identifier matched using the dictionary comparer.
        /// </param>
        /// <returns>
        /// A lazy sequence of borrowed mutable alias descriptors, empty when the export has no aliases.
        /// </returns>
        public IEnumerable<FunctionAlias> EnumerateFunctionAliases(string name)
        {
            if (!this.functionAliases.TryGetValue(name, out var aliases))
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
