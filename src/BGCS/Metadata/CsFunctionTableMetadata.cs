using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Collections;

namespace BGCS.Metadata
{
    using Newtonsoft.Json;

    /// <summary>
    /// Retains the ordered dynamic-import slot-to-symbol assignments produced by generation.
    /// </summary>
    public class CsFunctionTableMetadata : GeneratorMetadataEntry, ICloneable<CsFunctionTableMetadata>
    {
        /// <summary>
        /// Retains a caller-supplied mutable function-table entry list.
        /// </summary>
        /// <param name="entries">
        /// The ordered slot-to-symbol assignments retained without copying.
        /// </param>
        [JsonConstructor]
        public CsFunctionTableMetadata(List<CsFunctionTableEntry> entries)
        {
            this.entries = entries;
        }

        /// <summary>
        /// Creates an empty dynamic-import table.
        /// </summary>
        public CsFunctionTableMetadata()
        {
            this.entries = [];
        }

        /// <summary>
        /// Gets or sets the mutable ordered slot-to-symbol assignments; both slots and symbols must be unique.
        /// </summary>
        public List<CsFunctionTableEntry> entries { get; set; }

        /// <summary>
        /// Validates all incoming slot and symbol assignments before appending independent copies; identical assignments are ignored.
        /// </summary>
        /// <param name="from">
        /// The source table retained unchanged.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The source table is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The destination already contains duplicate slots or symbols.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// An incoming slot or symbol conflicts with the destination or another incoming assignment; the destination remains unchanged.
        /// </exception>
        public void Merge(CsFunctionTableMetadata from)
        {
            ArgumentNullException.ThrowIfNull(from);
            Dictionary<int, CsFunctionTableEntry> indicesLookupTable = entries.ToDictionary(entry => entry.index);
            Dictionary<string, CsFunctionTableEntry> entryPointLookupTable = entries.ToDictionary(entry => entry.entryPoint, StringComparer.Ordinal);
            List<CsFunctionTableEntry> additions = [];
            foreach (CsFunctionTableEntry entry in from.entries)
            {
                if (entryPointLookupTable.TryGetValue(entry.entryPoint, out var tableEntry))
                {
                    if (tableEntry.index != entry.index)
                    {
                        throw new InvalidOperationException($"Duplicate entry point found '{entry.entryPoint}' with not the same index '{entry.index}' vs '{tableEntry.index}'.");
                    }

                    continue;
                }

                if (indicesLookupTable.TryGetValue(entry.index, out tableEntry))
                {
                    if (tableEntry.entryPoint != entry.entryPoint)
                    {
                        throw new InvalidOperationException($"Duplicate index found '{entry.index}' with not the same entry point '{entry.entryPoint}' vs '{tableEntry.entryPoint}'.");
                    }

                    continue;
                }

                CsFunctionTableEntry clone = entry.Clone();
                indicesLookupTable.Add(clone.index, clone);
                entryPointLookupTable.Add(clone.entryPoint, clone);
                additions.Add(clone);
            }

            entries.AddRange(additions);
        }

        CsFunctionTableMetadata ICloneable<CsFunctionTableMetadata>.Clone()
        {
            return new(this.entries.Select(x => x.Clone()).ToList());
        }

        /// <summary>
        /// Copies the entry container and each mutable slot-to-symbol assignment.
        /// </summary>
        /// <returns>
        /// A separately mutable table with the same ordered assignments.
        /// </returns>
        public override GeneratorMetadataEntry Clone()
        {
            return new CsFunctionTableMetadata(this.entries.Select(x => x.Clone()).ToList());
        }

        /// <summary>
        /// Merges compatible table metadata only when optional function-table merging is enabled.
        /// </summary>
        /// <param name="from">
        /// The candidate metadata entry; incompatible entry types are ignored.
        /// </param>
        /// <param name="options">
        /// The policy whose mergeFunctionTable flag enables table merging.
        /// </param>
        public override void Merge(
            GeneratorMetadataEntry from,
            in MergeOptions options
        ) {
            if (options.mergeFunctionTable && from is CsFunctionTableMetadata metadata)
            {
                Merge(metadata);
            }
        }
    }
}
