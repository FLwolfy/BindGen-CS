using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using Microsoft.Build.Framework;

namespace BGCS.MSBuild;

/// <summary>Clears complete verified owned intermediate/deployment sets through the SDK publication protocol.</summary>
public sealed class ClearBindingsTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private readonly object m_gate = new();
    private CancellationTokenSource? m_cancellation;
    private bool m_cancelled;

    /// <summary>Gets or sets the grouped configuration identifying the expected generated-output owner.</summary>
    [Required]
    public string ConfigFile { get; set; } = string.Empty;
    /// <summary>Gets or sets the captured consumer project directory.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets the captured inner-build intermediate root bounding generated cleanup.</summary>
    [Required]
    public string IntermediateRoot { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact generated output root beneath the intermediate root.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets the application output root bounding deployed-worker cleanup.</summary>
    [Required]
    public string ApplicationOutputRoot { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact deployed-worker root beneath application output.</summary>
    [Required]
    public string WorkerOutputRoot { get; set; } = string.Empty;
    /// <summary>Gets or sets the SDK-provided worker source root, which cleanup must protect.</summary>
    public string? WorkerSourceRoot { get; set; }

    /// <inheritdoc/>
    public override bool Execute()
    {
        using CancellationTokenSource cancellation = new();
        lock (m_gate)
        {
            m_cancellation = cancellation;
            if (m_cancelled) cancellation.Cancel();
        }
        try { return ExecuteAsync(cancellation.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { Log.LogError("BGCS cleanup was cancelled."); return false; }
        catch (Exception exception) { Log.LogErrorFromException(exception, showStackTrace: false); return false; }
        finally { lock (m_gate) m_cancellation = null; }
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        lock (m_gate)
        {
            m_cancelled = true;
            m_cancellation?.Cancel();
        }
    }

    private async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        string project = Path.GetFullPath(ProjectDirectory);
        string output = RequireDescendant(OutputDirectory, IntermediateRoot, project);
        string worker = RequireDescendant(WorkerOutputRoot, ApplicationOutputRoot, project);
        if (Directory.Exists(output))
        {
            GenerationRequest request = await GenerationRequest.FromFileAsync(Path.GetFullPath(ConfigFile, project), cancellationToken: cancellationToken).ConfigureAwait(false);
            BindingConfiguration configuration = request.configuration with { output = request.configuration.output with { root = output } };
            await using BindingHost host = BindingHost.CreateDefault();
            await host.ClearOwnedOutputAsync(request.WithConfiguration(configuration), cancellationToken).ConfigureAwait(false);
        }
        if (!string.IsNullOrWhiteSpace(WorkerSourceRoot) && Directory.Exists(worker))
            WorkerAssetDeployment.Clear(Path.GetFullPath(WorkerSourceRoot, project), worker, cancellationToken);
        return !Log.HasLoggedErrors;
    }

    private static string RequireDescendant(
        string childPath,
        string parentPath,
        string project
    ) {
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath, project));
        string child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(childPath, project));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!child.StartsWith(parent + Path.DirectorySeparatorChar, comparison))
            throw new IOException("BGCS cleanup must stay strictly beneath its captured inner-build intermediate or application output root.");
        return child;
    }
}
