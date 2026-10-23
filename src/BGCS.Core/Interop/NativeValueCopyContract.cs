using BGCS.Core.Common;

namespace BGCS.Core.Interop;

/// <summary>Preserves a checked native by-value operation implemented through a temporary pointer carrier.</summary>
/// <param name="bridgeExport">The exact generated native bridge operation.</param>
/// <param name="sourceCallable">The unchanged original source callable.</param>
/// <param name="sourceParameter">The original by-value parameter, or null for the original return value.</param>
/// <param name="sourceType">The original source value type, separately from the physical carrier.</param>
/// <param name="storageType">The exact reparsed pointee storage.</param>
/// <param name="direction">Input for a source argument or Output for the original result.</param>
/// <param name="evidence">The bridge contract validation that established this copy relationship.</param>
/// <remarks>This relationship permits a value copy; it never certifies a direct aggregate calling ABI or a borrowed alias conversion.</remarks>
public sealed record NativeValueCopyContract(
    StableId bridgeExport,
    StableId sourceCallable,
    StableId? sourceParameter,
    StableId sourceType,
    StableId storageType,
    NativeParameterDirection direction,
    Evidence evidence
);
