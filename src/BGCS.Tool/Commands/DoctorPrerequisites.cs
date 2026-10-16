using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Capabilities;

namespace BGCS.Tool.Commands;

internal static class DoctorPrerequisites
{
    internal sealed record WorkerReadiness(
        bool ready,
        string status,
        string root,
        string? hostRid,
        string? parserIdentity,
        ImmutableArray<BindingDiagnostic> diagnostics
    )
    {
        public bool assetDigestsVerified => ready;
        public bool parseExecuted => false;
    }

    internal sealed record ToolVersionEvidence(
        string role,
        string id,
        string executable,
        string? version,
        bool versionProbeExecuted
    );
    internal sealed record NativeReadiness(
        string targetId,
        string providerId,
        bool? ready,
        string status,
        string? compilerPath,
        string? compilerVersion,
        string? sysRoot,
        ImmutableArray<ToolVersionEvidence> tools,
        ImmutableArray<BindingDiagnostic> diagnostics
    )
    {
        public bool nativeBuildExecuted => false;
        public bool invocationExecuted => false;
        public bool sourceViewRequired => string.Equals(Path.GetFileName(compilerPath), "cl.exe", StringComparison.OrdinalIgnoreCase);
        public bool sourceViewExecuted => false;
        public string sourceViewPrerequisites => sourceViewRequired
            ? "The selected installed MSVC compiler must support conformant /Zc:preprocessor, /PD macro definitions and /C comment preservation. Initial native preprocessing is required for generate and analyze even when nativeBuild.mode=none; doctor does not execute that capability probe."
            : "No selected MSVC initial source-view prerequisite; this inspection does not execute parsing or independent native fact verification.";
        public string prerequisiteScope => "provider-declared tool files and resolved SDK/header paths; implicit driver-internal tools and native compilation are not independently validated";
    }

    internal static WorkerReadiness InspectWorker(string applicationBase)
    {
        string root = Path.GetFullPath(Path.Combine(applicationBase, "bgcs", "worker"));
        string manifestPath = Path.Combine(root, "worker-assets.manifest.json");
        if (!File.Exists(manifestPath))
            return new(false, Directory.Exists(root) ? "unlocked" : "missing", root, null, null,
                [new(BindingDiagnosticSeverity.Error,
                    "A complete locked application-deployed worker is required. Restore/deploy the BGCS SDK assets or reinstall the host-specific tool; three entry files alone do not establish parser readiness.",
                    "BGCS-DOCTOR-WORKER")]);
        try
        {
            WorkerAssetDeployer deployer = new();
            WorkerAssetManifest manifest = deployer.ReadManifest(manifestPath);
            deployer.VerifyDeployment(root, manifest, RuntimeInformation.RuntimeIdentifier);
            return new(true, "verified", root, manifest.hostRid, manifest.parserIdentity, []);
        }
        catch (Exception failure)
        {
            return new(false, "invalid", root, null, null,
                [new(BindingDiagnosticSeverity.Error,
                    $"The deployed parser closure is invalid: {failure.Message} Restore the exact matching host assets before parsing.",
                    "BGCS-DOCTOR-WORKER")]);
        }
    }

    internal static async Task<NativeReadiness> InspectNativeAsync(
        GenerationRequest request,
        TargetInspection inspection,
        CancellationToken cancellationToken
    ) {
        NativeTargetDescriptor? target = inspection.resolvedTarget;
        string configuredProvider = request.configuration.nativeBuild.provider;
        if (target is null)
            return new(inspection.targetId, configuredProvider, false, "unresolved", null, null, null, [],
                [new(BindingDiagnosticSeverity.Error, "The Host did not retain the resolved target/toolchain descriptor for prerequisite inspection.",
                    "BGCS-DOCTOR-TARGET")]);
        if (string.Equals(Path.GetFileName(target.toolchain.compilerPath), "cl.exe", StringComparison.OrdinalIgnoreCase)
            && !File.Exists(target.toolchain.compilerPath))
            return Result(false, "missingSourceViewCompiler", configuredProvider,
                [new(BindingDiagnosticSeverity.Error,
                    "The selected MSVC compiler is required for initial source preprocessing even when native building is disabled. Restore the pinned cl.exe and its SDK before generation.",
                    "BGCS-DOCTOR-NATIVE")]);
        if (request.configuration.nativeBuild.mode == "none")
            return Result(true, "notRequested", configuredProvider);
        if (request.configuration.nativeBuild.mode == "prebuilt")
        {
            string? missing = request.configuration.nativeBuild.artifacts
                .Select(path => Path.GetFullPath(path, request.baseDirectory)).FirstOrDefault(path => !File.Exists(path));
            return missing is null ? Result(true, "prebuiltInputsPresentArtifactValidationNotExecuted", configuredProvider)
                : Result(false, "missingPrebuiltInput", configuredProvider,
                    [new(BindingDiagnosticSeverity.Error, $"The declared prebuilt native artifact '{missing}' is missing.", "BGCS-DOCTOR-NATIVE")]);
        }
        ImmutableArray<string> extensions = inspection.extensionNativeBuildProviders;
        if (configuredProvider == "auto" && extensions.Length > 1)
            return Result(false, "ambiguousRegisteredProviders", configuredProvider,
                [new(BindingDiagnosticSeverity.Error, "Multiple native build providers are registered. Select their exact service identity before native prerequisite inspection.",
                    "BGCS-DOCTOR-NATIVE")]);
        string? customProvider = configuredProvider == "auto" && extensions.Length == 1 ? extensions[0]
            : extensions.Contains(configuredProvider, StringComparer.Ordinal) ? configuredProvider : null;
        if (customProvider is not null)
            return Result(null, "requiresHostProviderPreflight", customProvider,
                [new(BindingDiagnosticSeverity.Warning,
                    $"Custom provider '{customProvider}' needs a matching Host-owned native plan preflight. The CLI prerequisite probe does not instantiate a separate plugin registry or assume the provider's tools. No native build was executed.",
                    "BGCS-DOCTOR-NATIVE-PENDING")]);
        if (configuredProvider is not ("auto" or "clang" or "gnu" or "msvc" or "clang-cl" or "cmake" or "meson" or "msbuild"))
            return Result(false, "unregisteredProvider", configuredProvider,
                [new(BindingDiagnosticSeverity.Error, $"Native build provider '{configuredProvider}' is not registered in the inspected Host composition.",
                    "BGCS-DOCTOR-NATIVE")]);
        INativeBuildProvider? provider = null;
        ImmutableArray<ToolVersionEvidence>.Builder tools = ImmutableArray.CreateBuilder<ToolVersionEvidence>();
        try
        {
            if (target.toolchain.sysRoot is string sysroot && !Directory.Exists(sysroot))
                throw new DirectoryNotFoundException($"The pinned target sysroot '{sysroot}' is missing.");
            if ((target.platformId is "android" or "emscripten" || target.abiId == "musl") && target.toolchain.sysRoot is null)
                throw new InvalidOperationException("The declared native target requires an explicit matching SDK sysroot for construction.");
            foreach (string include in target.toolchain.systemIncludeFolders.Concat(target.toolchain.cxxSystemIncludeFolders))
                if (!Directory.Exists(include)) throw new DirectoryNotFoundException($"The pinned target system header directory '{include}' is missing.");
            string? buildTool = LocalPath(request.configuration.nativeBuild.buildToolPath);
            string? auxiliary = LocalPath(request.configuration.nativeBuild.auxiliaryToolPath);
            provider = new NativeBuildProviderResolver().Resolve(target, configuredProvider, buildTool, auxiliary);
            NativeOutputKind kind = request.configuration.nativeBuild.outputKind ?? PlatformCapabilityCatalog.Get(target).defaultOutputKind;
            if (provider is not INativeBuildPrerequisiteProvider described)
                return Result(null, "requiresProviderPrerequisiteDescription", provider.id,
                    [new(BindingDiagnosticSeverity.Warning, "The resolved provider has no exact prerequisite description; a Host-owned plan preflight is required.",
                        "BGCS-DOCTOR-NATIVE-PENDING")]);
            foreach (NativeToolPrerequisite tool in described.DescribePrerequisites())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tool.requiredOutputKind is not null && tool.requiredOutputKind != kind) continue;
                if (tool.path is not string path || !Path.IsPathFullyQualified(path) || !File.Exists(path))
                    throw new FileNotFoundException($"The native provider's exact '{tool.role}' prerequisite '{tool.id}' is missing.", tool.path);
                if (tool.role is "buildSystem" or "backend")
                {
                    Version? minimum = tool.minimumVersion is null ? null : Version.Parse(tool.minimumVersion);
                    tools.Add(await ProbeAsync(tool.role, tool.id, path, tool.id == "msbuild" ? ["-version", "-nologo"] : ["--version"],
                        minimum, request.baseDirectory, cancellationToken).ConfigureAwait(false));
                }
                else tools.Add(new(tool.role, tool.id, path, tool.role == "compiler" ? target.toolchain.compilerVersion : null, false));
            }
            return Result(true, "resolvedPrerequisitesBuildNotExecuted", provider.id, tools: tools.ToImmutable());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure)
        {
            return Result(false, "unavailable", configuredProvider,
                [new(BindingDiagnosticSeverity.Error,
                    $"Native provider prerequisites for '{inspection.targetId}' are unavailable: {failure.Message} Install the declared compiler/SDK/build tools or correct their pinned paths; BGCS does not download them.",
                    "BGCS-DOCTOR-NATIVE")], tools.ToImmutable());
        }
        finally
        {
            if (provider is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
            else if (provider is IDisposable disposable) disposable.Dispose();
        }

        string? LocalPath(string? value) => value is null ? null : Path.GetFullPath(value, request.baseDirectory);
        NativeReadiness Result(
            bool? ready,
            string status,
            string providerId,
            ImmutableArray<BindingDiagnostic> diagnostics = default,
            ImmutableArray<ToolVersionEvidence> tools = default
        )
            => new(inspection.targetId, providerId, ready, status, target.toolchain.compilerPath, target.toolchain.compilerVersion,
                target.toolchain.sysRoot, tools.IsDefault ? [] : tools, diagnostics.IsDefault ? [] : diagnostics);
    }

    internal static async Task<ToolVersionEvidence> ProbeAsync(
        string role,
        string id,
        string executable,
        ImmutableArray<string> arguments,
        Version? minimum,
        string workingDirectory,
        CancellationToken cancellationToken
    ) {
        ProcessExecutionResult result = await ProcessExecutor.ExecuteAsync(executable, arguments, workingDirectory,
            TimeSpan.FromSeconds(10), cancellationToken, maximumCapturedCharacters: 65536).ConfigureAwait(false);
        if (result.timedOut || result.exitCode != 0)
            throw new IOException($"The '{role}' version probe failed or timed out for '{executable}'.");
        string version = ExtractVersion(id, result.standardOutput + "\n" + result.standardError, minimum);
        return new(role, id, executable, version, true);
    }

    internal static string ExtractVersion(
        string role,
        string output,
        Version? minimum
    ) {
        string pattern = role == "cmake" ? @"(?m)^cmake version ([0-9]+\.[0-9]+(?:\.[0-9]+){0,2})\b"
            : @"\b([0-9]+\.[0-9]+(?:\.[0-9]+){0,2})\b";
        Match match = Regex.Match(output, pattern,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out Version? version))
            throw new IOException($"The '{role}' executable did not report a supported numeric version.");
        if (minimum is not null && version < minimum)
            throw new IOException($"The '{role}' executable reports {version}; at least {minimum} is required.");
        return version.ToString();
    }
}
