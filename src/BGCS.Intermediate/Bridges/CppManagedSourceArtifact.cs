namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Freezes one managed source contribution before output publication begins.
/// </summary>
/// <param name="relativePath">Source path relative to the managed extension output directory.</param>
/// <param name="content">Complete source text copied from the extension contribution.</param>
public sealed record CppManagedSourceArtifact(
    string relativePath,
    string content
);
