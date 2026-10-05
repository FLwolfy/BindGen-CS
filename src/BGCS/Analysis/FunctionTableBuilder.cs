using System;
using System.Collections.Generic;

namespace BGCS.Analysis
{
    using System.Linq;
    using BGCS.Metadata;

    /// <summary>
    /// Defines the public class <c>FunctionTableBuilder</c>.
    /// </summary>
    internal sealed class FunctionTableBuilder
    {
        private int m_index;
        private readonly List<CsFunctionTableEntry> m_entries = [];
        private readonly Dictionary<string, int> m_entryPointToIndex = [];
        /// <summary>
        /// Initializes a new instance of <see cref = "FunctionTableBuilder"/>.
        /// </summary>
        public FunctionTableBuilder()
        {
        }

        /// <summary>
        /// Executes public operation <c>FunctionTableBuilder</c>.
        /// </summary>
        public FunctionTableBuilder(int funcTableableStart)
        {
            this.m_index = funcTableableStart;
        }

        /// <summary>
        /// Exposes public member <c>entries</c>.
        /// </summary>
        internal IReadOnlyList<CsFunctionTableEntry> entries => this.m_entries;

        /// <summary>
        /// Executes public operation <c>Append</c>.
        /// </summary>
        public void Append(List<CsFunctionTableEntry> functionTableEntries)
        {
            HashSet<int> usedIndices = [.. this.m_entries.Select(x => x.index)];
            foreach (var entry in functionTableEntries)
            {
                if (this.m_entryPointToIndex.ContainsKey(entry.entryPoint))
                {
                    continue;
                }

                int resolvedIndex = entry.index;
                if (resolvedIndex < 0 || usedIndices.Contains(resolvedIndex))
                {
                    resolvedIndex = this.m_index;
                    while (usedIndices.Contains(resolvedIndex))
                    {
                        resolvedIndex++;
                    }
                }

                entry.index = resolvedIndex;
                this.m_entryPointToIndex.Add(entry.entryPoint, resolvedIndex);
                this.m_entries.Add(entry);
                usedIndices.Add(resolvedIndex);
                this.m_index = Math.Max(this.m_index, resolvedIndex + 1);
            }
        }

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public int Add(string name)
        {
            if (this.m_entryPointToIndex.TryGetValue(name, out var id))
            {
                return id;
            }

            id = this.m_index;
            this.m_entries.Add(new(id, name));
            this.m_entryPointToIndex.Add(name, id);
            this.m_index++;
            return id;
        }

    }
}
