namespace BGCS;

/// <summary>
/// Controls whether unresolved native safety semantics are reported as warnings or generation-blocking errors.
/// </summary>
public enum StrictSafetySeverity
{
    /// <summary>Reports unresolved semantics while preserving raw ABI generation.</summary>
    Warning,

    /// <summary>Preserves raw ABI generation but suppresses high-risk friendly overloads.</summary>
    SuppressFriendly,

    /// <summary>Rejects generation until the required minimal mapping is supplied.</summary>
    Error
}
