namespace BGCS
{
    /// <summary>
    /// Defines values for <c>BoolType</c>.
    /// </summary>
    public enum BoolType
    {
        /// <summary>
        /// Uses the generated runtime's one-byte boolean wrapper.
        /// </summary>
        Bool8,

        /// <summary>
        /// Uses the generated runtime's four-byte boolean wrapper.
        /// </summary>
        Bool32,

        /// <summary>
        /// Uses the unmanaged <see cref="byte"/> representation directly.
        /// </summary>
        Byte,

        /// <summary>
        /// Uses the unmanaged <see cref="int"/> representation directly.
        /// </summary>
        Int32,
    }
}
