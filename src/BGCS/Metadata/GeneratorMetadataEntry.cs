namespace BGCS.Metadata
{
    /// <summary>
    /// Defines the public class <c>GeneratorMetadataEntry</c>.
    /// </summary>
    public abstract class GeneratorMetadataEntry
    {
        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public abstract GeneratorMetadataEntry Clone();

        /// <summary>
        /// Merges configuration or metadata via <c>Merge</c>.
        /// </summary>
        public abstract void Merge(GeneratorMetadataEntry from, in MergeOptions options);
    }

    /// <summary>
    /// Defines the public class <c>GeneratorMetadataEntry</c>.
    /// </summary>
    public abstract class GeneratorMetadataEntry<T> : GeneratorMetadataEntry where T : GeneratorMetadataEntry
    {
        /// <summary>
        /// Merges configuration or metadata via <c>Merge</c>.
        /// </summary>
        public override void Merge(GeneratorMetadataEntry from, in MergeOptions options)
        {
            if (from is T t)
            {
                Merge(t, options);
            }
        }

        /// <summary>
        /// Merges configuration or metadata via <c>Merge</c>.
        /// </summary>
        public abstract void Merge(T from, in MergeOptions options);
    }
}
