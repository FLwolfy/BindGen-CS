using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Maps verified native files to candidate package assets through a host-owned artifact sink.</summary>
public interface INativeArtifactPackager
{
    /// <summary>Gets the stable asset packager identity.</summary>
    string id { get; }
    /// <summary>Verifies content and architecture, then writes the complete candidate asset set.</summary>
    /// <param name="request">The immutable verified-artifact set and candidate sink.</param>
    /// <param name="cancellationToken">Cancels native file reading and layout generation.</param>
    /// <returns>The complete native asset and build metadata manifest.</returns>
    ValueTask<NativeAssetPackageResult> PackageAsync(
        NativeArtifactPackagingRequest request,
        CancellationToken cancellationToken
    );
}
