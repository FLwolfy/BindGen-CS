using System;
using System.IO;
using System.Runtime.InteropServices;
using BGCS.Infrastructure.Verification;

namespace BGCS.Infrastructure.Workers;

/// <summary>Locates explicitly supplied or application-deployed worker assets without consulting global installations.</summary>
public static class WorkerAssetLocator
{
    /// <summary>Locates the managed worker entry and its execution manifests.</summary>
    /// <param name="explicitRoot">An explicitly deployed worker directory, or null for application assets.</param>
    /// <param name="applicationBase">The application base directory, or null for the current application base.</param>
    /// <returns>The absolute worker entry assembly.</returns>
    /// <exception cref="FileNotFoundException">The worker or an execution manifest is missing.</exception>
    public static string Locate(
        string? explicitRoot = null,
        string? applicationBase = null
    ) {
        string root = explicitRoot is null
            ? Path.Combine(applicationBase ?? AppContext.BaseDirectory, "bgcs", "worker")
            : Path.GetFullPath(explicitRoot);
        const string entryName = "BGCS.Frontend.Clang.Worker";
        foreach (string extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
        {
            string path = Path.Combine(root, entryName + extension);
            if (!File.Exists(path))
                throw new FileNotFoundException("The application-deployed BGCS worker is incomplete. Deploy the SDK worker assets or set an explicit worker asset root.", path);
        }
        string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
        if (File.Exists(manifestPath))
        {
            var manifest = new WorkerAssetDeployer().ReadManifest(manifestPath);
            new WorkerAssetDeployer().VerifyDeployment(root, manifest, RuntimeInformation.RuntimeIdentifier);
        }
        return Path.Combine(root, entryName + ".dll");
    }
}
