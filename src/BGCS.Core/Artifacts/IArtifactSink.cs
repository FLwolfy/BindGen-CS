using System;

namespace BGCS.Core.Artifacts;

/// <summary>Writes logical output artifacts without exposing physical filesystem operations to a backend.</summary>
public interface IArtifactSink
{
    /// <summary>Publishes one source-text artifact into the caller's candidate store.</summary>
    /// <param name="relativePath">A portable path relative to the candidate root.</param>
    /// <param name="content">The complete artifact text.</param>
    /// <returns>The caller's artifact location or identity.</returns>
    string WriteText(
        string relativePath,
        string content
    );
    /// <summary>Writes one complete binary candidate using the same path and collision policy as source artifacts.</summary>
    /// <param name="relativePath">The portable candidate-relative artifact path.</param>
    /// <param name="content">The binary artifact bytes.</param>
    /// <returns>The caller's artifact location or identity.</returns>
    string WriteBytes(
        string relativePath,
        ReadOnlyMemory<byte> content
    )
        => throw new NotSupportedException("This artifact sink does not support binary candidates.");
}
