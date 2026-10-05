using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Owns the comparison between generated bridge declarations and an actual binary export table.
/// </summary>
/// <param name="tool">
/// Inspection executable used to read the binary export table.
/// </param>
/// <param name="expected">
/// Required bridge symbols from the public headers.
/// </param>
/// <param name="actual">
/// Symbols found in the binary export table.
/// </param>
/// <param name="missing">
/// Required symbols absent from the binary export table.
/// </param>
public sealed record NativeExportInspectionResult(
    string tool,
    IReadOnlyList<string> expected,
    IReadOnlyList<string> actual,
    IReadOnlyList<string> missing
)
{
    /// <summary>
    /// Required bridge symbols from the public headers. The sequence is copied and cannot be changed through this snapshot.
    /// </summary>
    public IReadOnlyList<string> expected { get; } = Array.AsReadOnly((expected
        ?? throw new ArgumentNullException(nameof(expected))).ToArray());

    /// <summary>
    /// Symbols found in the binary export table. The sequence is copied and cannot be changed through this snapshot.
    /// </summary>
    public IReadOnlyList<string> actual { get; } = Array.AsReadOnly((actual
        ?? throw new ArgumentNullException(nameof(actual))).ToArray());

    /// <summary>
    /// Required symbols absent from the binary export table. The sequence is copied and cannot be changed through this snapshot.
    /// </summary>
    public IReadOnlyList<string> missing { get; } = Array.AsReadOnly((missing
        ?? throw new ArgumentNullException(nameof(missing))).ToArray());

    /// <summary>
    /// Gets whether every required bridge symbol was found in the binary export table.
    /// </summary>
    public bool success => missing.Count == 0;
}
