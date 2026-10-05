namespace BGCS.Core.Extensibility;

/// <summary>
/// Describes extension state that affects incremental generation results.
/// </summary>
public interface ICacheFingerprintProvider
{
    /// <summary>
    /// Captures the current output-affecting configuration without changing it.
    /// </summary>
    /// <returns>
    /// A deterministic state description; empty means that no additional state affects output.
    /// </returns>
    string GetCacheFingerprint();
}
