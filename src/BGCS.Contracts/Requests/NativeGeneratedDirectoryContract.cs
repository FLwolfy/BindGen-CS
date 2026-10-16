using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Requests;

/// <summary>Locks one generated file independently of its temporary materialization path.</summary>
/// <param name="relativePath">The portable path within its declared generated directory.</param>
/// <param name="length">The exact generated file length.</param>
/// <param name="digest">The SHA-256 of its complete contents.</param>
/// <param name="logicalSourcePath">An explicit portable source identity for frontend facts, or null for a non-source artifact.</param>
public sealed record NativeGeneratedFileContract(
    string relativePath,
    long length,
    ContentDigest digest,
    string? logicalSourcePath = null
);

/// <summary>Locks the complete published membership of an explicitly relocated generated input directory.</summary>
/// <param name="identity">The stable contribution role identity, independent of temporary storage.</param>
/// <param name="path">The artifact:/ directory whose files must exactly match this contract.</param>
/// <param name="files">Every permitted file, including other declared generator artifacts in the same role directory.</param>
public sealed record NativeGeneratedDirectoryContract(
    string identity,
    string path,
    ImmutableArray<NativeGeneratedFileContract> files
);
