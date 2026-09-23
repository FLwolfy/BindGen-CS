namespace BGCS.Intermediate;

/// <summary>Identifies how the managed ABI surface reaches native entry points.</summary>
public enum BindingImportMode
{
    DllImport,
    LibraryImport,
    FunctionTable
}

/// <summary>Describes one deterministic function-table slot.</summary>
/// <param name="Index">Zero-based table slot.</param>
/// <param name="EntryPoint">Native symbol loaded into the slot.</param>
public sealed record BindingFunctionTableEntry(int Index, string EntryPoint);
