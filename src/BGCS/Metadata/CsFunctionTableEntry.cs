namespace BGCS.Metadata
{
    /// <summary>
    /// Associates one dynamic-import table slot with its exact native entry-point symbol.
    /// </summary>
    public class CsFunctionTableEntry
    {
        /// <summary>
        /// Retains the slot and symbol assigned during generation.
        /// </summary>
        /// <param name="index">
        /// The zero-based function-table slot.
        /// </param>
        /// <param name="entryPoint">
        /// The exact native symbol resolved by dynamic import.
        /// </param>
        public CsFunctionTableEntry(
            int index,
            string entryPoint
        ) {
            this.index = index;
            this.entryPoint = entryPoint;
        }

        /// <summary>
        /// Gets or sets the zero-based dynamic-import table slot.
        /// </summary>
        public int index { get; set; }
        /// <summary>
        /// Gets or sets the exact native symbol resolved by dynamic import.
        /// </summary>
        public string entryPoint { get; set; }

        /// <summary>
        /// Copies the table slot and symbol into a separately mutable entry.
        /// </summary>
        /// <returns>
        /// An independent entry retaining the same immutable symbol string.
        /// </returns>
        public CsFunctionTableEntry Clone()
        {
            return new(this.index, this.entryPoint);
        }
    }
}
