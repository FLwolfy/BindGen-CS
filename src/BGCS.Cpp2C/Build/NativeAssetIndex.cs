using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Owns the deterministic native asset entries committed beside the runtime asset tree.
/// </summary>
/// <param name="assets">
/// Asset entries copied into a read-only snapshot.
/// </param>
public sealed record NativeAssetIndex(
    IReadOnlyList<NativeAssetEntry> assets
)
{
    /// <summary>
    /// Asset entries copied into a read-only snapshot. The sequence is copied and cannot be changed through this snapshot.
    /// </summary>
    public IReadOnlyList<NativeAssetEntry> assets { get; } = Array.AsReadOnly((assets
        ?? throw new ArgumentNullException(nameof(assets))).ToArray());

}
