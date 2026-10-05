namespace BGCS.Intermediate;

/// <summary>Identifies how the managed ABI surface reaches native entry points.</summary>
public enum BindingImportMode
{
    /// <summary>
    /// The runtime resolves traditional P/Invoke imports.
    /// </summary>
    DllImport,
    /// <summary>
    /// The .NET source generator emits P/Invoke stubs.
    /// </summary>
    LibraryImport,
    /// <summary>
    /// The consumer resolves symbols into deterministic unmanaged function-pointer slots.
    /// </summary>
    FunctionTable
}

/// <summary>Describes one deterministic function-table slot.</summary>
/// <param name = "index">Zero-based table slot.</param>
/// <param name = "entryPoint">Native symbol loaded into the slot.</param>
public sealed record BindingFunctionTableEntry(
    int index,
    string entryPoint
);
