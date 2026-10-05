namespace BGCS.Metadata
{
    /// <summary>
    /// Controls optional contributions when compatible generation metadata entries are combined.
    /// </summary>
    public struct MergeOptions
    {
        /// <summary>
        /// Whether function-table entries participate in metadata merging.
        /// </summary>
        public bool mergeFunctionTable;
    }
}
