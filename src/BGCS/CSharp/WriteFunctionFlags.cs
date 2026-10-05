namespace BGCS.CSharp
{
    /// <summary>
    /// Selects receiver and handle presentation when planning managed callable projections.
    /// </summary>
    public enum WriteFunctionFlags
    {
        /// <summary>
        /// Emits a standalone callable without receiver transformations.
        /// </summary>
        None = 0,
        /// <summary>
        /// Uses a managed handle as the native receiver.
        /// </summary>
        UseHandle = 1,
        /// <summary>
        /// Supplies the containing instance as the native receiver.
        /// </summary>
        UseThis = 2,
        /// <summary>
        /// Emits a managed extension-method receiver.
        /// </summary>
        Extension = 4,
    }
}
