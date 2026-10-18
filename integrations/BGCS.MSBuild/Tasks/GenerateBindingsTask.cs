using System;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Configuration;
using BGCS.Contracts;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BGCS.MSBuild;

/// <summary>Runs the public Host pipeline in a standalone .NET TaskHost and supplies exact generated Compile items.</summary>
public sealed class GenerateBindingsTask : Task, ICancelableTask
{
    private readonly object m_gate = new();
    private CancellationTokenSource? m_cancellation;
    private bool m_cancelled;

    /// <summary>Gets or sets the absolute grouped configuration input.</summary>
    [Required]
    public string ConfigFile { get; set; } = string.Empty;
    /// <summary>Gets or sets the captured project directory.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets the inner-build intermediate output root.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;
    /// <summary>Gets or sets an explicit deployed parser worker directory.</summary>
    public string? WorkerAssetRoot { get; set; }
    /// <summary>Gets or sets the selected native target for a multi-target configuration.</summary>
    public string? NativeTarget { get; set; }
    /// <summary>Gets exact generated source items after complete candidate publication.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];
    /// <summary>Gets the complete publication ownership manifest.</summary>
    [Output]
    public string ArtifactManifest { get; private set; } = string.Empty;
    /// <summary>Gets the publication's stable logical ownership identity.</summary>
    [Output]
    public string LibraryId { get; private set; } = string.Empty;
    /// <inheritdoc/>
    public override bool Execute()
    {
        using CancellationTokenSource cancellation = new();
        lock (m_gate)
        {
            m_cancellation = cancellation;
            if (m_cancelled) cancellation.Cancel();
        }
        try
        {
            return ExecuteAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Log.LogError("BGCS generation was cancelled.");
            return false;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
        finally
        {
            lock (m_gate) m_cancellation = null;
        }
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

    private async System.Threading.Tasks.Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        string project = Path.GetFullPath(ProjectDirectory);
        string input = Path.GetFullPath(ConfigFile, project);
        GenerationRequest request = await GenerationRequest.FromFileAsync(input, cancellationToken: cancellationToken).ConfigureAwait(false);
        BindingConfiguration configuration = request.configuration;
        if (!string.IsNullOrEmpty(NativeTarget))
        {
            TargetConfiguration selected = configuration.targets.Single(target => target.id == NativeTarget);
            configuration = configuration with { targets = [selected] };
        }
        else if (configuration.targets.Length != 1)
            throw new ArgumentException("MSBuild inner builds must select exactly one BGCSNativeTarget from a multi-target configuration.");
        configuration = configuration with { output = configuration.output with { root = Path.GetFullPath(OutputDirectory, project) } };
        BindingHostBuilder builder = BindingHost.CreateBuilder().AddBuiltIns();
        if (!string.IsNullOrEmpty(WorkerAssetRoot)) builder.WithWorkerAssetRoot(Path.GetFullPath(WorkerAssetRoot, project));
        await using BindingHost host = builder.Build();
        GenerationRequest selectedRequest = request.WithConfiguration(configuration);
        GenerationResult result = await host.GenerateAsync(selectedRequest, cancellationToken).ConfigureAwait(false);
        foreach (BindingDiagnostic diagnostic in result.diagnostics)
        {
            if (diagnostic.isFailure) Log.LogError(null, diagnostic.code, null, diagnostic.location?.file ?? input,
                diagnostic.location?.line ?? 0, diagnostic.location?.column ?? 0, 0, 0, diagnostic.message);
            else if (diagnostic.severity == BindingDiagnosticSeverity.Warning) Log.LogWarning(null, diagnostic.code, null,
                diagnostic.location?.file ?? input, diagnostic.location?.line ?? 0, diagnostic.location?.column ?? 0, 0, 0, diagnostic.message);
            else Log.LogMessage(MessageImportance.Low, "{0}: {1}", diagnostic.code, diagnostic.message);
        }
        if (!result.success) return false;
        LibraryId = configuration.library.id;
        GeneratedFiles = result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
            .Select(artifact =>
            {
                TaskItem item = new(Path.GetFullPath(artifact.relativePath, result.outputRoot));
                item.SetMetadata("AutoGen", "true");
                item.SetMetadata("Visible", "false");
                return (ITaskItem)item;
            }).ToArray();
        ArtifactManifest = Path.Combine(result.outputRoot, ".bgcs-artifacts.json");
        return !Log.HasLoggedErrors;
    }
}
