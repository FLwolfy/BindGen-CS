using System;
using System.Runtime.InteropServices;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Hosting;

namespace BGCS.Frontend.Clang.Workers;

/// <summary>Provides explicit application deployment for the SDK's isolated parser execution assets.</summary>
public static class WorkerAssetDeployment
{
    /// <summary>Clears a verified deployed worker as a complete owned set while protecting the original packaged resources.</summary>
    /// <param name="sourceRoot">The original packaged worker closure, which must not overlap the deployment.</param>
    /// <param name="destinationRoot">The explicitly deployed application worker directory.</param>
    /// <param name="host">The explicitly selected session host facilities.</param>
    /// <param name="cancellationToken">Cancels before owned replacement.</param>
    /// <returns>Whether an existing owned worker was cleared; a valid empty marker remains for redeployment.</returns>
    public static bool Clear(
        string sourceRoot,
        string destinationRoot,
        HostServices host,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        return BGCS.Infrastructure.Publishing.ArtifactPublisher.ClearOwned(destinationRoot, "bgcs-worker:" + host.execution.hostRuntimeIdentifier,
            host.paths, [System.IO.Path.GetFullPath(sourceRoot)], cancellationToken);
    }
    /// <summary>Deploys the exact packaged host closure independently of the requested native generation target.</summary>
    /// <param name="sourceRoot">The explicit package-provided worker asset root.</param>
    /// <param name="manifestPath">The package's locked worker closure manifest.</param>
    /// <param name="destinationRoot">The application worker directory.</param>
    /// <param name="host">The explicitly selected session host facilities.</param>
    /// <param name="cancellationToken">Cancels copy and publication.</param>
    public static void Deploy(
        string sourceRoot,
        string manifestPath,
        string destinationRoot,
        HostServices host,
        CancellationToken cancellationToken = default
    ) {
        WorkerAssetDeployer deployer = new(host.paths);
        WorkerAssetManifest manifest = deployer.ReadManifest(manifestPath);
        deployer.Deploy(sourceRoot, destinationRoot, manifest, host.execution.hostRuntimeIdentifier, cancellationToken);
    }
}
