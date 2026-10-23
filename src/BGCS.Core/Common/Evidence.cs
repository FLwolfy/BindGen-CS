namespace BGCS.Core.Common;

/// <summary>Distinguishes missing, inferred, declared and independently verified information.</summary>
public enum EvidenceLevel
{
    /// <summary>No fact is available.</summary>
    Unknown,
    /// <summary>A heuristic inferred the fact.</summary>
    Inferred,
    /// <summary>Structured native input declares the fact.</summary>
    Declared,
    /// <summary>The user explicitly asserts the fact.</summary>
    UserAsserted,
    /// <summary>An independent verification procedure established the fact.</summary>
    Verified
}

/// <summary>Records where a fact came from and the strength of that evidence.</summary>
/// <param name="level">The evidence strength.</param>
/// <param name="origin">The input, rule or verification receipt identity.</param>
public sealed record Evidence(
    EvidenceLevel level,
    string origin
);
