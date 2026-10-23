namespace BGCS.Core.Hosting;

/// <summary>Captures host-local physical resolution separately from logical names and comparison keys.</summary>
/// <param name="logicalPath">The normalized absolute name supplied by the caller.</param>
/// <param name="physicalPath">The absolute name after bounded symbolic-link resolution.</param>
/// <param name="identityKey">The ordinal key produced under observed directory name semantics.</param>
public sealed record HostPathIdentity(
    string logicalPath,
    string physicalPath,
    string identityKey
);
