namespace BGCS.Metadata
{
    /// <summary>
    /// Defines the public class <c>CsFunctionTableEntry</c>.
    /// </summary>
    public class CsFunctionTableEntry
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CsFunctionTableEntry"/>.
        /// </summary>
        public CsFunctionTableEntry(int index, string entryPoint)
        {
            Index = index;
            EntryPoint = entryPoint;
        }

        /// <summary>
        /// Gets or sets <c>Index</c>.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets <c>EntryPoint</c>.
        /// </summary>
        public string EntryPoint { get; set; }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsFunctionTableEntry Clone()
        {
            return new(Index, EntryPoint);
        }
    }
}
