using BGCS.Core.Managed;

namespace BGCS.Core.Requests;

/// <summary>Captures source-generation inputs without exposing the Host, mutable configuration or output directories.</summary>
/// <param name="libraryId">The logical library identity.</param>
/// <param name="nativeLibraryName">The configured native library base name.</param>
/// <param name="source">Frozen target and effective source input facts; internal output exclusions are removed by the Host.</param>
/// <param name="managedNamespace">The effective managed API namespace.</param>
/// <param name="apiName">The effective API identity.</param>
/// <param name="runtimeNamespace">The effective Runtime namespace for package or source consumption.</param>
public sealed record SourceContributionRequest(
    string libraryId,
    string nativeLibraryName,
    FrontendRequest source,
    string managedNamespace,
    string apiName,
    string runtimeNamespace
)
{
    /// <summary>Gets the configured public API lifetime style so contributed helpers can choose an appropriate declaration.</summary>
    public ManagedApiStyle apiStyle { get; init; } = ManagedApiStyle.Instance;
}
