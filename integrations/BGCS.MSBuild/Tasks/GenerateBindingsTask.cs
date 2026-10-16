using System;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
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
    /// <summary>Gets or sets whether explicitly declared source/prebuilt native inputs are validated and staged.</summary>
    public bool BuildNative { get; set; } = true;
    /// <summary>Gets or sets whether the consumer is publishing a NativeAOT application.</summary>
    public bool PublishAot { get; set; }
    /// <summary>Gets exact generated source items after complete candidate publication.</summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];
    /// <summary>Gets the complete publication ownership manifest.</summary>
    [Output]
    public string ArtifactManifest { get; private set; } = string.Empty;
    /// <summary>Gets the publication's stable logical ownership identity.</summary>
    [Output]
    public string LibraryId { get; private set; } = string.Empty;
    /// <summary>Gets static native inputs for browser WebAssembly.</summary>
    [Output]
    public ITaskItem[] NativeFileReferences { get; private set; } = [];
    /// <summary>Gets static native inputs for Apple mobile consumers.</summary>
    [Output]
    public ITaskItem[] NativeReferences { get; private set; } = [];
    /// <summary>Gets native dynamic libraries to copy beside the managed consumer.</summary>
    [Output]
    public ITaskItem[] NativeLibraryFiles { get; private set; } = [];
    /// <summary>Gets archive/object inputs for the consumer's NativeAOT native linker.</summary>
    [Output]
    public ITaskItem[] NativeAotLibraries { get; private set; } = [];

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
        GenerationResult result = BuildNative && configuration.nativeBuild.mode != "none"
            ? (await host.BuildAsync(new BuildRequest(selectedRequest), cancellationToken).ConfigureAwait(false)).generation
            : await host.GenerateAsync(selectedRequest, cancellationToken).ConfigureAwait(false);
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
        var native = result.nativeReferences.Select(reference =>
        {
            if (!result.artifacts.Any(artifact => artifact.relativePath == reference.relativePath && artifact.digest == reference.digest))
                throw new InvalidOperationException("A native reference is not part of the exact verified publication.");
            TaskItem item = new(Path.GetFullPath(reference.relativePath, result.outputRoot));
            item.SetMetadata("BGCSTarget", reference.target.targetId.value);
            item.SetMetadata("BGCSRuntimeIdentifier", reference.runtimeIdentifier);
            if (string.IsNullOrWhiteSpace(reference.importModuleName))
                throw new InvalidOperationException("A native reference has no declared import module identity.");
            if (reference.kind == NativeReferenceKind.BrowserNativeFile
                && !string.Equals(reference.importModuleName, Path.GetFileNameWithoutExtension(item.ItemSpec), StringComparison.Ordinal))
                throw new InvalidOperationException("The declared browser import module differs from the exact native reference filename.");
            item.SetMetadata("BGCSPInvokeModule", reference.importModuleName);
            return (Reference: reference, Item: (ITaskItem)item);
        }).ToArray();
        NativeFileReferences = native.Where(artifact => artifact.Reference.kind == NativeReferenceKind.BrowserNativeFile).Select(artifact => artifact.Item).ToArray();
        NativeReferences = native.Where(artifact => artifact.Reference.kind == NativeReferenceKind.AppleStatic).Select(artifact =>
            {
                artifact.Item.SetMetadata("Kind", "Static");
                artifact.Item.SetMetadata("ForceLoad", "true");
                return artifact.Item;
            }).ToArray();
        NativeLibraryFiles = native.Where(artifact => artifact.Reference.kind == NativeReferenceKind.DynamicCopy)
            .Select(artifact => artifact.Item).ToArray();
        NativeAotLibraries = native.Where(artifact => artifact.Reference.kind == NativeReferenceKind.NativeAotLibrary)
            .Select(artifact => artifact.Item).ToArray();
        if (NativeAotLibraries.Length != 0 && !PublishAot)
            throw new InvalidOperationException("Static desktop native inputs require the consuming project to enable PublishAot.");
        if (native.Any(artifact => !Enum.IsDefined(artifact.Reference.kind)))
            throw new InvalidOperationException("The native publication requests an unsupported integration reference kind.");
        ArtifactManifest = Path.Combine(result.outputRoot, ".bgcs-artifacts.json");
        return !Log.HasLoggedErrors;
    }
}
