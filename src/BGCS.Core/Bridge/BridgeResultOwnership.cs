using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Requests resolution of a real native release declaration for an explicitly allocated recipe result.</summary>
/// <param name="nativeFunction">The exact qualified native release function name.</param>
/// <param name="allocatorDomain">The explicit allocation/release domain identity.</param>
public sealed record CppResultReleaseRequest(
    string nativeFunction,
    string allocatorDomain
)
{
    /// <summary>Gets any for explicitly asserted thread independence or unknown to suppress automatic owning wrappers.</summary>
    public string releaseThreadDomain { get; init; } = "unknown";
    /// <summary>Gets the explicit assertion; source conversion alone cannot prove ownership or thread independence.</summary>
    public Evidence evidence { get; init; } = new(EvidenceLevel.UserAsserted, "owned-native-recipe");
}

/// <summary>Pairs an owned C result with an actual typed release export and its allocation domain.</summary>
/// <param name="releaseExport">The exact verified bridge release export identity.</param>
/// <param name="allocatorDomain">The explicitly matched allocation and release domain.</param>
/// <param name="releaseThreadDomain">The asserted release domain; null or unknown omits automatic owning wrappers.</param>
/// <param name="evidence">The ownership assertion provenance.</param>
public sealed record BridgeResultOwnership(
    StableId releaseExport,
    StableId allocatorDomain,
    string? releaseThreadDomain,
    Evidence evidence
);
