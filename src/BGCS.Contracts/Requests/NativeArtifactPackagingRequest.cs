using System.Collections.Immutable;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Targets;

namespace BGCS.Contracts.Requests;

/// <summary>Names a content-identified native artifact selected for package layout.</summary>
/// <param name="path">The existing complete artifact path.</param>
/// <param name="targetId">The artifact's concrete target identity.</param>
/// <param name="outputKind">Its dynamic, archive or object consumption form.</param>
/// <param name="digest">The exact content identity supplied by the producing verification stage.</param>
public sealed record NativeAssetInput(
    string path,
    NativeTargetId targetId,
    NativeOutputKind outputKind,
    ContentDigest digest
)
{
    /// <summary>Gets resolved platform, architecture and ABI facts for opaque target identities; null selects a built-in identity adapter.</summary>
    public NativeTargetDescriptor? target { get; init; }
}

/// <summary>Requests candidate asset packaging without granting formal output ownership.</summary>
/// <param name="artifacts">The complete native artifact set.</param>
/// <param name="sink">The host-owned package candidate sink.</param>
public sealed record NativeArtifactPackagingRequest(
    ImmutableArray<NativeAssetInput> artifacts,
    IArtifactSink sink
);

/// <summary>Describes a packaged native asset and the build item required by its consumer.</summary>
/// <param name="targetId">The concrete native ABI identity.</param>
/// <param name="runtimeIdentifier">The .NET asset-selection RID.</param>
/// <param name="relativePath">The exact package-relative artifact path.</param>
/// <param name="digest">The native file content identity.</param>
/// <param name="outputKind">The native consumption form.</param>
/// <param name="buildItem">The required native build item, or null for dynamic RID assets.</param>
public sealed record NativePackageAsset(
    NativeTargetId targetId,
    string runtimeIdentifier,
    string relativePath,
    ContentDigest digest,
    NativeOutputKind outputKind,
    string? buildItem
)
{
    /// <summary>Gets preserved resolved target facts used to verify and prepare this asset.</summary>
    public NativeTargetDescriptor? target { get; init; }
}

/// <summary>Returns complete candidate package entries and manifests without a platform certification claim.</summary>
/// <param name="assets">All native package entries in stable order.</param>
/// <param name="metadataArtifacts">The asset manifest and generated build-target descriptors.</param>
public sealed record NativeAssetPackageResult(
    ImmutableArray<NativePackageAsset> assets,
    ImmutableArray<ArtifactDescriptor> metadataArtifacts
);
