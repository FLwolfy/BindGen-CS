namespace BGCS.Metadata
{
    /// <summary>
    /// Defines cloning and merge behavior for one mutable named generation metadata contribution.
    /// </summary>
    public abstract class GeneratorMetadataEntry
    {
        /// <summary>
        /// Copies the entry's mutable container and any value descriptors covered by its cloning policy.
        /// </summary>
        /// <returns>
        /// An independently mutable entry; the concrete entry documents whether element references are retained.
        /// </returns>
        public abstract GeneratorMetadataEntry Clone();
        /// <summary>
        /// Combines compatible incoming metadata according to the concrete entry's merge policy.
        /// </summary>
        /// <param name="from">
        /// The source entry; implementations retain it unchanged.
        /// </param>
        /// <param name="options">
        /// Options controlling optional contributions such as function-table entries.
        /// </param>
        public abstract void Merge(
            GeneratorMetadataEntry from,
            in MergeOptions options
        );
    }

    /// <summary>
    /// Dispatches a merge only when the incoming metadata has the expected entry type.
    /// </summary>
    /// <typeparam name="T">The compatible metadata entry type.</typeparam>
    public abstract class GeneratorMetadataEntry<T> : GeneratorMetadataEntry where T : GeneratorMetadataEntry
    {
        /// <summary>
        /// Dispatches typed metadata merging, leaving this entry unchanged for an incompatible source type.
        /// </summary>
        /// <param name="from">
        /// The candidate metadata entry.
        /// </param>
        /// <param name="options">
        /// Options forwarded to a compatible typed merge.
        /// </param>
        public override void Merge(
            GeneratorMetadataEntry from,
            in MergeOptions options
        ) {
            if (from is T t)
            {
                Merge(t, options);
            }
        }

        /// <summary>
        /// Merges a compatible source using the concrete entry's ownership and conflict policy.
        /// </summary>
        /// <param name="from">
        /// The compatible source retained unchanged by this operation.
        /// </param>
        /// <param name="options">
        /// Options controlling optional metadata contributions.
        /// </param>
        public abstract void Merge(
            T from,
            in MergeOptions options
        );
    }
}
