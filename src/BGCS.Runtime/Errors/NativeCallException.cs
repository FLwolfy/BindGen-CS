using System;

namespace BGCS.Runtime;

/// <summary>Reports a declared native status failure after its owned error channel has been released.</summary>
public sealed class NativeCallException : Exception
{
    /// <summary>Records the exact failed native export and status without guessing an error-text encoding.</summary>
    /// <param name="status">The native failure status.</param>
    /// <param name="exportName">The exact native export identity.</param>
    public NativeCallException(
        int status,
        string exportName
    ) : base($"Native export '{exportName}' failed with status {status}.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);
        this.status = status;
        this.exportName = exportName;
    }
    /// <summary>Gets the exact native status value.</summary>
    public int status { get; }
    /// <summary>Gets the failed native linker symbol.</summary>
    public string exportName { get; }
}
