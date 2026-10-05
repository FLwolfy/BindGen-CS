namespace BGCS.Configuration
{
    /// <summary>
    /// Selects generated DllImport, LibraryImport, or explicit function-table native invocation.
    /// </summary>
    public enum ImportType
    {
        /// <summary>
        /// Emits runtime-resolved DllImport entry points.
        /// </summary>
        DllImport,
        /// <summary>
        /// Emits source-generated LibraryImport entry points.
        /// </summary>
        LibraryImport,
        /// <summary>
        /// Resolves entry points through an explicit native function table.
        /// </summary>
        FunctionTable
    }
}
