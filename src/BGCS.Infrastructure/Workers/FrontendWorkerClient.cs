using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Workers;

/// <summary>Runs each native parse in a separate process and enforces cancellation and a deadline.</summary>
public sealed class FrontendWorkerClient : IFrontend
{
    private readonly string m_workerPath;
    private readonly string m_dotnetPath;
    private readonly TimeSpan m_timeout;
    private readonly string m_parserIdentity;
    private readonly object m_environmentGate = new();
    private ImmutableDictionary<string, string>? m_frozenEnvironment;
    private string? m_cacheDotnetPath;
    private string? m_cacheRuntimeVersion;

    /// <summary>Locks the deployed parser closure and exact child environment before detached facts can be cached.</summary>
    /// <param name="cancellationToken">Cancels execution-file verification.</param>
    /// <returns>The complete execution identity; unsupported ambient injection raises an explicit cache limitation.</returns>
    /// <exception cref="NotSupportedException">The worker is unlocked or uses ambient input mechanisms without a complete file closure.</exception>
    public WorkerExecutionIdentity CaptureExecutionIdentity(CancellationToken cancellationToken = default)
    {
        string root = Path.GetDirectoryName(m_workerPath)!;
        string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
        if (!File.Exists(manifestPath)) throw new NotSupportedException("Frontend caching requires a verified deployed worker asset manifest.");
        ImmutableDictionary<string, string> environment;
        lock (m_environmentGate)
        {
            if (m_frozenEnvironment is null)
            {
                ImmutableDictionary<string, string>.Builder captured = ImmutableDictionary.CreateBuilder<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                    captured.Add((string)entry.Key, (string)entry.Value!);
                captured.Remove("BGCS_CLANG_RUNTIME_DIR");
                m_frozenEnvironment = captured.ToImmutable();
            }
            environment = m_frozenEnvironment;
        }
        string[] opaqueInputs = ["CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "OBJC_INCLUDE_PATH", "INCLUDE",
            "SDKROOT", "GCC_EXEC_PREFIX", "COMPILER_PATH", "LIBRARY_PATH", "SOURCE_DATE_EPOCH",
            "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_STARTUP_HOOKS", "CORECLR_ENABLE_PROFILING", "COR_ENABLE_PROFILING"];
        foreach (string name in opaqueInputs)
            if (environment.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value))
                throw new NotSupportedException("Frontend caching cannot reuse the untracked ambient input mechanism: " + name);
        if (!m_workerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Frontend caching requires a managed worker with a pinned shared-runtime execution closure.");
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        DirectoryInfo runtime = new(runtimeDirectory);
        DirectoryInfo? shared = runtime.Parent?.Parent;
        if (runtime.Parent?.Name != "Microsoft.NETCore.App" || shared?.Name != "shared" || shared.Parent is null)
            throw new NotSupportedException("Frontend caching cannot identify the generator's shared .NET runtime installation.");
        string expectedHost = Path.Combine(shared.Parent.FullName, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        string? selectedHost = ResolveHost(m_dotnetPath, environment);
        if (selectedHost is null || !PhysicalPathResolver.Resolve(selectedHost).Equals(PhysicalPathResolver.Resolve(expectedHost), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new NotSupportedException("Frontend caching requires the worker's dotnet host to match the captured generator runtime installation.");
        var deployment = new WorkerAssetDeployer().CaptureVerifiedDeployment(root, RuntimeInformation.RuntimeIdentifier,
            Path.ChangeExtension(m_workerPath, ".runtimeconfig.json"),
            [new(selectedHost, ExternalInputKind.File), new(runtimeDirectory, ExternalInputKind.Directory),
                new(Path.Combine(shared.Parent.FullName, "host", "fxr"), ExternalInputKind.Directory)], cancellationToken);
        WorkerAssetManifest manifest = deployment.Manifest;
        SourceSnapshot snapshot = deployment.Snapshot;
        using (JsonDocument configuration = JsonDocument.Parse(deployment.RuntimeConfiguration))
        {
            JsonElement options = configuration.RootElement.GetProperty("runtimeOptions");
            if (!options.TryGetProperty("framework", out JsonElement framework) || framework.GetProperty("name").GetString() != "Microsoft.NETCore.App"
                || options.TryGetProperty("frameworks", out _) || options.TryGetProperty("includedFrameworks", out _))
                throw new NotSupportedException("Frontend caching requires exactly one shared Microsoft.NETCore.App worker framework.");
        }
        lock (m_environmentGate)
        {
            m_cacheDotnetPath = selectedHost;
            m_cacheRuntimeVersion = runtime.Name;
        }
        var identity = new
        {
            workerPath = m_workerPath,
            m_parserIdentity,
            manifest.hostRid,
            protocol = WorkerProtocol.C_REVISION,
            model = BGCS.Contracts.Versioning.ModelRevision.C_NATIVE,
            runtime = Environment.Version.ToString(),
            environmentDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(environment.OrderBy(entry => entry.Key, StringComparer.Ordinal)))),
            snapshot
        };
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)))), snapshot);

        static string? ResolveHost(
            string executable,
            ImmutableDictionary<string, string> environment
        ) {
            if (Path.IsPathFullyQualified(executable)) return File.Exists(executable) ? executable : null;
            if (executable.Contains('/') || executable.Contains('\\')) return null;
            if (!environment.TryGetValue("PATH", out string? search)) return null;
            string name = OperatingSystem.IsWindows() && !Path.HasExtension(executable) ? executable + ".exe" : executable;
            foreach (string directory in search.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Path.IsPathFullyQualified(directory)) continue;
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    /// <summary>Creates a client for an explicitly deployed worker.</summary>
    /// <param name="workerPath">The worker executable or managed entry assembly.</param>
    /// <param name="timeout">A positive parse deadline, or null for five minutes.</param>
    /// <param name="dotnetPath">The .NET host used for a managed worker, or null for the current host.</param>
    /// <param name="parserIdentity">The pinned parser identity expected from the deployed worker.</param>
    /// <exception cref="FileNotFoundException">The worker entry point does not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The deadline is not positive.</exception>
    public FrontendWorkerClient(
        string workerPath,
        TimeSpan? timeout = null,
        string? dotnetPath = null,
        string parserIdentity = WorkerProtocol.C_PARSER_IDENTITY
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        m_workerPath = Path.GetFullPath(workerPath);
        if (!File.Exists(m_workerPath))
            throw new FileNotFoundException("The deployed Clang worker is unavailable.", m_workerPath);
        m_timeout = timeout ?? TimeSpan.FromSeconds(300);
        if (m_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        m_dotnetPath = dotnetPath ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        ArgumentException.ThrowIfNullOrWhiteSpace(parserIdentity);
        m_parserIdentity = parserIdentity;
    }

    /// <inheritdoc/>
    public async ValueTask<FrontendResult> ParseAsync(
        FrontendRequest request,
        CancellationToken cancellationToken
    ) {
        WorkerResponse response = await ExchangeAsync(request, WorkerOperationKind.Parse, cancellationToken).ConfigureAwait(false);
        return response.result!;
    }

    /// <summary>Discovers extension state in a separate worker without retaining an AST or loading plugin code in this process.</summary>
    /// <param name="request">The resolved target, locked worker activations and complete fingerprint context.</param>
    /// <param name="cancellationToken">Cancels startup, activation and transport.</param>
    /// <returns>Detached extension fingerprints and complete declared local input observations.</returns>
    public async ValueTask<WorkerPluginCapture> CapturePluginInputsAsync(
        FrontendRequest request,
        CancellationToken cancellationToken = default
    ) {
        WorkerResponse response = await ExchangeAsync(request, WorkerOperationKind.CapturePluginInputs, cancellationToken).ConfigureAwait(false);
        return response.pluginCapture!;
    }

    private async ValueTask<WorkerResponse> ExchangeAsync(
        FrontendRequest request,
        WorkerOperationKind operation,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.workerPlugins.IsDefault) throw new ArgumentException("Worker plugin activations must be initialized.", nameof(request));
        if (!request.workerPlugins.IsEmpty && request.workerPluginContext is null)
            throw new ArgumentException("Worker-only extensions require a complete frozen fingerprint context.", nameof(request));
        if (request.workerPlugins.IsEmpty && (request.workerPluginCapture is not null || request.workerPluginContext is not null))
            throw new ArgumentException("A worker extension context or capture requires declared worker activations.", nameof(request));
        if (operation == WorkerOperationKind.CapturePluginInputs && request.workerPlugins.IsEmpty)
            throw new ArgumentException("Extension input discovery requires at least one worker-only activation.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource deadline = new(m_timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        bool managedWorker = m_workerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        ProcessStartInfo start = new(managedWorker ? m_cacheDotnetPath ?? m_dotnetPath : m_workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = string.IsNullOrEmpty(request.pathBase) ? Path.GetDirectoryName(m_workerPath)! : Path.GetFullPath(request.pathBase)
        };
        if (managedWorker && m_cacheRuntimeVersion is not null)
        {
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add("--fx-version");
            start.ArgumentList.Add(m_cacheRuntimeVersion);
            start.ArgumentList.Add("--roll-forward");
            start.ArgumentList.Add("Disable");
        }
        if (managedWorker)
            start.ArgumentList.Add(m_workerPath);
        lock (m_environmentGate)
            if (m_frozenEnvironment is not null)
            {
                start.Environment.Clear();
                foreach ((string name, string value) in m_frozenEnvironment) start.Environment.Add(name, value);
            }
        // A verified deployed worker owns its parser closure. An unrelated inherited override must not replace it.
        if (File.Exists(Path.Combine(Path.GetDirectoryName(m_workerPath)!, "worker-assets.manifest.json")))
            start.Environment.Remove("BGCS_CLANG_RUNTIME_DIR");
        using Process process = new() { StartInfo = start };
        if (!process.Start())
            throw new IOException("The Clang worker could not be started.");
        using CancellationTokenSource outputDrain = new();
        Task<string> errorOutput = ReadBoundedErrorsAsync(process.StandardError, outputDrain.Token);
        string requestId = Guid.NewGuid().ToString("N");
        string? drainedErrors = null;
        try
        {
            WorkerHandshake handshake = await WorkerProtocol.ReadAsync<WorkerHandshake>(process.StandardOutput.BaseStream, linked.Token).ConfigureAwait(false);
            if (handshake.protocolRevision != WorkerProtocol.C_REVISION || handshake.modelRevision != BGCS.Contracts.Versioning.ModelRevision.C_NATIVE)
                throw new InvalidDataException("The deployed worker protocol or native model revision is incompatible.");
            string expectedVersion = typeof(FrontendWorkerClient).Assembly.GetName().Version?.ToString() ?? string.Empty;
            if (!string.Equals(handshake.buildVersion, expectedVersion, StringComparison.Ordinal))
                throw new InvalidDataException("The deployed worker belongs to a different BGCS build.");
            if (!string.Equals(handshake.parserIdentity, m_parserIdentity, StringComparison.Ordinal))
                throw new InvalidDataException("The deployed worker parser identity is incompatible.");
            if (!string.Equals(handshake.hostRid, RuntimeInformation.RuntimeIdentifier, StringComparison.Ordinal))
                throw new InvalidDataException("The deployed worker does not match the generator host RID.");
            FrontendRequest parseRequest = request with { workerPlugins = [], workerPluginContext = null, workerPluginCapture = null };
            await WorkerProtocol.WriteAsync(process.StandardInput.BaseStream, new WorkerParseRequest(requestId, parseRequest)
            {
                operation = operation,
                pluginActivations = request.workerPlugins,
                pluginContext = request.workerPluginContext,
                expectedPluginCapture = operation == WorkerOperationKind.Parse ? request.workerPluginCapture : null
            }, linked.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            WorkerResponse response = await WorkerProtocol.ReadAsync<WorkerResponse>(process.StandardOutput.BaseStream, linked.Token).ConfigureAwait(false);
            if (!string.Equals(response.requestId, requestId, StringComparison.Ordinal))
                throw new InvalidDataException("The worker response does not match the active request.");
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            string stderr = await errorOutput.WaitAsync(linked.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || response.error is not null || operation == WorkerOperationKind.Parse && response.result is null
                || operation == WorkerOperationKind.CapturePluginInputs && response.pluginCapture is null)
                throw WorkerFailure(requestId, process.ExitCode, response.error, stderr);
            if (operation == WorkerOperationKind.CapturePluginInputs && response.result is not null)
                throw new InvalidDataException("Worker extension input discovery returned an unexpected parsed model.");
            if (request.workerPlugins.IsEmpty && response.pluginCapture is not null)
                throw new InvalidDataException("The worker returned an undeclared extension activation result.");
            if (operation == WorkerOperationKind.Parse && (response.result!.module is null || response.result.snapshot is null
                || response.result.snapshot.files.IsDefault || response.result.snapshot.searchDirectories.IsDefault))
                throw new InvalidDataException($"Clang worker returned an incomplete result for request {requestId}.");
            if (!request.workerPlugins.IsEmpty)
            {
                WorkerPluginCapture capture = response.pluginCapture ?? throw new InvalidDataException("The worker omitted its extension input identity.");
                if (capture.fingerprints.IsDefault || capture.unfingerprintedServices.IsDefault || capture.inputs is null
                    || capture.inputs.files.IsDefault || capture.inputs.searchDirectories.IsDefault || capture.inputs.absentInputs.IsDefault)
                    throw new InvalidDataException("The worker returned an incomplete extension input identity.");
                if (operation == WorkerOperationKind.Parse && request.workerPluginCapture is { } expected
                    && !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(capture, WorkerProtocol.jsonOptions)))
                    throw new InvalidDataException("The parser worker's extension inputs changed after preflight discovery.");
            }
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Clang worker exceeded the {m_timeout.TotalSeconds}-second deadline for request {requestId}.");
        }
        catch (EndOfStreamException exception)
        {
            drainedErrors = await StopAndDrainAsync().ConfigureAwait(false);
            string exit = process.HasExited ? $" Exit code: {process.ExitCode}." : string.Empty;
            throw new IOException($"Clang worker closed an incomplete response for request {requestId}.{exit}",
                new IOException(string.IsNullOrWhiteSpace(drainedErrors) ? exception.Message : drainedErrors, exception));
        }
        catch (JsonException exception)
        {
            drainedErrors = await StopAndDrainAsync().ConfigureAwait(false);
            throw new InvalidDataException($"Clang worker returned invalid JSON for request {requestId}.",
                new IOException(string.IsNullOrWhiteSpace(drainedErrors) ? exception.Message : drainedErrors, exception));
        }
        finally
        {
            if (drainedErrors is null) await StopAndDrainAsync().ConfigureAwait(false);
        }

        async Task<string> StopAndDrainAsync()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // Natural worker exit may race with cancellation-driven process termination.
            }
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                return await errorOutput.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // An inherited pipe must not turn a parse deadline into an unbounded host wait.
                outputDrain.Cancel();
                process.StandardError.Close();
                return "[Worker termination or diagnostic drain exceeded its five-second bound.]";
            }
        }
    }

    private static InvalidDataException WorkerFailure(
        string requestId,
        int exitCode,
        string? error,
        string stderr
    ) {
        string summary = string.IsNullOrWhiteSpace(error) ? $"The worker exited with code {exitCode} without a complete successful response." : error;
        int newline = summary.IndexOfAny(['\r', '\n']);
        if (newline >= 0) summary = summary[..newline];
        if (summary.Length > 2048) summary = summary[..2048] + " [truncated]";
        string? details = !string.IsNullOrWhiteSpace(stderr) ? stderr : error != summary ? error : null;
        if (details?.Length > 64 * 1024)
            details = "[Earlier worker diagnostics truncated.]" + Environment.NewLine + details[^(64 * 1024)..];
        return new InvalidDataException($"Clang worker failed for request {requestId}: {summary}",
            details is null ? null : new IOException(details));
    }

    private static async Task<string> ReadBoundedErrorsAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    ) {
        const int maximumCharacters = 64 * 1024;
        StringBuilder tail = new();
        char[] buffer = new char[4096];
        bool truncated = false;
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;
            tail.Append(buffer, 0, count);
            if (tail.Length > maximumCharacters)
            {
                tail.Remove(0, tail.Length - maximumCharacters);
                truncated = true;
            }
        }
        return (truncated ? "[Earlier worker diagnostics truncated.]" + Environment.NewLine : string.Empty) + tail;
    }
}

/// <summary>Pairs a non-secret execution digest with independently verifiable deployed parser and runtime bytes.</summary>
/// <param name="digest">The parser, protocol, managed runtime, host and frozen child-environment identity.</param>
/// <param name="snapshot">The complete locked worker execution files.</param>
public sealed record WorkerExecutionIdentity(
    ContentDigest digest,
    SourceSnapshot snapshot
);
