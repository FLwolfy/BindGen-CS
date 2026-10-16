using System.Collections.Immutable;

namespace BGCS.Contracts.Inputs;

/// <summary>Freezes parser input bytes and include-search membership without retaining file handles.</summary>
/// <param name="files">Actual input files in stable path order.</param>
/// <param name="searchDirectories">Search roots in declared precedence order.</param>
public sealed record SourceSnapshot(
    ImmutableArray<InputDependency> files,
    ImmutableArray<DirectoryInputDependency> searchDirectories
)
{
    /// <summary>Gets host-owned private candidate roots omitted only from search-directory membership; read file bytes remain tracked.</summary>
    public ImmutableArray<string> excludedDirectories { get; init; } = [];
    /// <summary>Gets explicitly declared absent inputs whose later creation changes the computation.</summary>
    public ImmutableArray<MissingInputDependency> absentInputs { get; init; } = [];
}
