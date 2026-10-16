using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using BGCS.Contracts.Artifacts;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BGCS.MSBuild;

/// <summary>Deploys the exact SDK-provided worker manifest without selecting parser resources from the native target.</summary>
public sealed class PrepareWorkerAssetsTask : Task
{
    /// <summary>Gets or sets the explicit package worker asset source root.</summary>
    [Required]
    public string SourceRoot { get; set; } = string.Empty;
    /// <summary>Gets or sets the locked SDK worker manifest path.</summary>
    [Required]
    public string ManifestPath { get; set; } = string.Empty;
    /// <summary>Gets or sets the application deployment root.</summary>
    [Required]
    public string DestinationRoot { get; set; } = string.Empty;
    /// <summary>Gets or sets the restored native parser package root for locked SDK resources.</summary>
    public string? NativePackageRoot { get; set; }
    /// <summary>Gets or sets the explicit native parser package version.</summary>
    public string ParserRuntimeVersion { get; set; } = "20.1.2";
    /// <summary>Gets or sets the exact supplemental parser package version.</summary>
    public string? SupplementalRuntimeVersion { get; set; }
    /// <summary>Gets or sets the intermediate closure assembly directory.</summary>
    public string? AssemblyRoot { get; set; }
    /// <summary>Gets the actual application-deployed worker directory.</summary>
    [Output]
    public string DeployedRoot { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public override bool Execute()
    {
        string? temporary = null;
        try
        {
            string source = Path.GetFullPath(SourceRoot);
            if (!string.IsNullOrWhiteSpace(NativePackageRoot))
            {
                if (AssemblyRoot is null) throw new ArgumentException("A restored package closure requires an explicit intermediate AssemblyRoot.");
                WorkerAssetManifest manifest = JsonSerializer.Deserialize<WorkerAssetManifest>(File.ReadAllText(ManifestPath))
                    ?? throw new IOException("The locked worker asset manifest is empty.");
                string host = RuntimeInformation.RuntimeIdentifier;
                if (manifest.hostRid != host) throw new IOException("The worker manifest belongs to another generator host.");
                string assemblyBase = Path.GetFullPath(AssemblyRoot);
                if (assemblyBase == source || assemblyBase == Path.GetFullPath(DestinationRoot)) throw new ArgumentException("The assembly and deployment roots must be distinct.");
                string assembled = Path.Combine(assemblyBase, "session-" + Guid.NewGuid().ToString("N"));
                temporary = assembled;
                Directory.CreateDirectory(assembled);
                foreach (ArtifactDescriptor artifact in manifest.artifacts)
                {
                    string relative = ValidateRelativePath(artifact.relativePath);
                    string input = Path.Combine(source, relative);
                    if (relative.StartsWith("runtimes/", StringComparison.Ordinal))
                    {
                        string file = Path.GetFileName(relative);
                        string package = file.StartsWith("libClangSharp", StringComparison.Ordinal) ? "libclangsharp.runtime." : "libclang.runtime.";
                        package += host;
                        string version = ParserRuntimeVersion;
                        if (host == "osx-x64") { package = "bgcs.parserruntime.osx-x64"; version = SupplementalRuntimeVersion ?? throw new ArgumentException("The Intel macOS supplemental runtime version is not configured."); }
                        input = Path.Combine(Path.GetFullPath(NativePackageRoot), package, version, relative);
                        if (!File.Exists(input) && host != "osx-x64")
                        {
                            string alternate = package.StartsWith("libclangsharp.", StringComparison.Ordinal) ? "libclang.runtime." + host : "libclangsharp.runtime." + host;
                            input = Path.Combine(Path.GetFullPath(NativePackageRoot), alternate, version, relative);
                        }
                    }
                    string destination = Path.Combine(assembled, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(input, destination, overwrite: true);
                }
                source = assembled;
            }
            WorkerAssetDeployment.Deploy(source, ManifestPath, DestinationRoot);
            DeployedRoot = Path.GetFullPath(DestinationRoot);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static string ValidateRelativePath(string path)
    {
        string[] parts = path.Split('/');
        if (Path.IsPathRooted(path) || path.Contains('\\') || parts.Any(part => part is "" or "." or ".." || part.Contains(':')))
            throw new IOException("The worker manifest contains an invalid relative path.");
        return path;
    }
}
