namespace BGCS.Intermediate;

/// <summary>
/// Controls how generated C# surfaces handle unresolved native safety semantics.
/// </summary>
public enum StrictSafetySeverity
{
    /// <summary>Reports unresolved semantics while preserving inferred friendly overloads.</summary>
    Warning,

    /// <summary>Preserves raw ABI generation but suppresses high-risk friendly overloads.</summary>
    SuppressFriendly,

    /// <summary>Rejects generation until the required minimal mapping is supplied.</summary>
    Error
}
