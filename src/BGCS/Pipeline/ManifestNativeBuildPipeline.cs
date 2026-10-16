using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Composition;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Workers;
using BGCS.NativeBuild;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Packaging;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Toolchains;
using BGCS.Infrastructure.Plugins;
using Plan = BGCS.Contracts.Requests.NativeBuildPlan;
using INativeBuildProvider = BGCS.Contracts.Ports.INativeBuildProvider;

namespace BGCS;

/// <summary>Plans native construction from verified generation manifests without reopening parser state.</summary>
public sealed partial class BindingHost
{
    /// <summary>Creates a complete shell-free native plan from a locked neutral generation manifest.</summary>
    /// <param name="request">The manifest and explicit native overrides.</param>
    /// <param name="cancellationToken">Cancels manifest verification, service activation or planning.</param>
    /// <returns>The private construction plan and declared final native artifact path.</returns>
    public async Task<ManifestNativeBuildPlanResult> PlanNativeBuildAsync(
        ManifestNativeBuildRequest request,
        CancellationToken cancellationToken = default
    ) {
        ManifestNativeBuildResult result = await ExecuteManifestNativeBuildAsync(request, dryRun: true, cancellationToken).ConfigureAwait(false);
        return new(result.plan, result.diagnostics) { nativeArtifact = result.nativeArtifact };
    }

    /// <summary>Builds and verifies native manifest inputs before jointly publishing all requested outputs.</summary>
    /// <param name="request">The locked manifest and explicit native overrides.</param>
    /// <param name="cancellationToken">Cancels tools, hashing and publication before installation starts.</param>
    /// <returns>The exact published native artifact, symbol evidence and package assets.</returns>
    public Task<ManifestNativeBuildResult> NativeBuildAsync(
        ManifestNativeBuildRequest request,
        CancellationToken cancellationToken = default
    )
        => ExecuteManifestNativeBuildAsync(request, dryRun: false, cancellationToken);

    private async Task<ManifestNativeBuildResult> ExecuteManifestNativeBuildAsync(
        ManifestNativeBuildRequest request,
        bool dryRun,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_gate) { ObjectDisposedException.ThrowIf(m_closing, this); m_sessions++; }
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        string scratch = Path.Combine(Path.GetTempPath(), "bgcs-manifest-native-" + Guid.NewGuid().ToString("N"));
        Plan? plan = null;
        string? final = null;
        NativeExportInspectionResult? exports = null;
        ImmutableArray<ArtifactDescriptor> packageArtifacts = [];
        ImmutableArray<NativeStringProofReceipt> literalProofs = [];
        bool published = false;
        GenerationComposition? composition = null;
        INativeBuildProvider? provider = null;
        INativeArtifactPackager? packager = null;
        GenerationInputTracker? extensionInputs = null;
        FrozenExtensionRegistry? definition = null;
        ArtifactSetPublisher? publisher = null;
        try
        {
            if (request.timeoutSeconds <= 0 || request.timeoutSeconds > 4_294_967) throw new ArgumentOutOfRangeException(nameof(request.timeoutSeconds));
            cancellation.CancelAfter(TimeSpan.FromSeconds(request.timeoutSeconds));
            LoadedNativeBuildManifest loaded = NativeBuildManifestLoader.Load(request.manifestPath, cancellation.Token);
            NativeBuildManifest manifest = loaded.manifest;
            definition = NativeManifestPluginComposition.Create(m_registry, manifest);
            extensionInputs = new(manifest.generationConfiguration ?? JsonSerializer.SerializeToElement(manifest, WorkerProtocol.jsonOptions),
                manifest.generationPathBase ?? Path.GetDirectoryName(Path.GetFullPath(request.manifestPath))!);
            NativeTargetDescriptor target = manifest.target;
            string selectedProvider = request.providerId == "auto" ? manifest.providerId : request.providerId;
            if (request.compilerPath is string overrideCompiler)
            {
                if (!Path.IsPathFullyQualified(overrideCompiler)) throw new ArgumentException("A compiler override must be an absolute executable path.");
                NativeToolchainDescriptor original = target.toolchain;
                NativeToolchainDescriptor toolchain = new(overrideCompiler, original.sysRoot, original.systemIncludeFolders, original.defines, original.arguments, original.cxxSystemIncludeFolders,
                    original.sdkVersion, string.Equals(original.compilerPath, overrideCompiler, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? original.compilerVersion : null,
                    original.linkerPath, original.libraryDirectories);
                target = new(target.targetId, target.platformId, target.architectureId, target.abiId, target.triple, toolchain, target.capabilities, target.byteOrder);
            }
            string filename = manifest.nativeArtifactFilename ?? BGCS.Platforms.Assets.NativeOutputLayout.ResolveFilename(manifest.libraryName, target, manifest.outputKind);
            bool ownsArtifactRoot = File.Exists(Path.Combine(loaded.artifactRoot, ".bgcs-artifacts.json"));
            string publicationRoot = ownsArtifactRoot ? loaded.artifactRoot : Path.Combine(loaded.artifactRoot, ".bgcs-native-build", target.targetId.value);
            final = request.outputFile is null ? Path.Combine(publicationRoot, ownsArtifactRoot ? target.targetId.value + "/native/bin/" : string.Empty, filename)
                : RequireAbsolute(request.outputFile, "native output");
            if (manifest.managedRuntime?.kind == ManagedRuntimeKind.BrowserWasm)
            {
                string actualIdentity = BGCS.Platforms.Assets.NativeOutputLayout.ResolveImportModuleName(target, manifest.managedRuntime, manifest.importModuleName!, Path.GetFileName(final));
                if (!string.Equals(actualIdentity, manifest.importModuleName, StringComparison.Ordinal))
                    throw new InvalidOperationException("The requested browser native output filename changes the generated managed import identity.");
            }
            target = ToolchainResolver.ResolveToolchain(target, manifest.sources.Any(source => source.language == SourceLanguage.Cpp) ? SourceLanguage.Cpp : SourceLanguage.C, selectedProvider);
            bool directoryOutput = request.outputFile is null || ownsArtifactRoot && Within(final, loaded.artifactRoot);
            string outputDestination = directoryOutput ? publicationRoot : final;
            string? packageRoot = request.packageRoot is null ? null : RequireAbsolute(request.packageRoot, "native package root");
            if (packageRoot is not null && string.Equals(packageRoot, outputDestination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("The package asset root must be separate from the native artifact publication root.");
            bool packageInsideOutput = packageRoot is not null && directoryOutput && Within(packageRoot, outputDestination);
            composition = await GenerationComposition.CreateAsync(definition, cancellation.Token, extensionInputs).ConfigureAwait(false);
            if (!composition.registry.Validators.IsEmpty && manifest.interop is null)
                throw new InvalidOperationException("Native manifest validation gates require the locked original InteropModule; the request cannot bypass them.");
            NativeProviderRegistration? registration = composition.registry.NativeBuildProviders.SingleOrDefault(item => item.Id == selectedProvider);
            provider = registration is null ? new NativeBuildProviderResolver().Resolve(target, selectedProvider, request.buildToolPath ?? manifest.buildToolPath, request.auxiliaryToolPath ?? manifest.auxiliaryToolPath)
                : registration.Factory() ?? throw new InvalidOperationException("The native provider factory returned no service.");
            if (registration is not null && provider.id != registration.Id) throw new InvalidOperationException("A native provider changed its frozen service identity.");
            extensionInputs.Capture(provider, target, cancellation.Token);
            if (!dryRun)
            {
                List<ArtifactSetDestination> destinations = [new(outputDestination, manifest.libraryId, directoryOutput)];
                if (packageRoot is not null && !packageInsideOutput) destinations.Add(new(packageRoot, manifest.libraryId + "/native-assets"));
                IEnumerable<string> protectedInputs = loaded.snapshot.files.Select(input => input.path)
                    .Concat(loaded.manifest.extensionInputs.SelectMany(snapshot => snapshot.searchDirectories.Select(input => input.path)))
                    .Concat(loaded.snapshot.absentInputs.Select(input => input.path))
                    .Where(path => !directoryOutput || !Within(path, outputDestination));
                publisher = new(destinations, protectedInputs, cancellation.Token);
            }
            Directory.CreateDirectory(scratch);
            NativeBuildRequest construction = new(target, manifest.sources.Select(source => new NativeCompilationUnit(NativeBuildManifestLoader.Resolve(source.path, loaded.artifactRoot), source.language, source.standard)).ToImmutableArray(),
                scratch, Path.Combine(scratch, Path.GetFileName(final)))
            {
                argumentWorkingDirectory = manifest.generationPathBase ?? Path.GetDirectoryName(Path.GetFullPath(request.manifestPath))!,
                preprocessingSources = manifest.preprocessingSources.Select(source => new NativeCompilationUnit(NativeBuildManifestLoader.Resolve(source.path, loaded.artifactRoot), source.language, source.standard)).ToImmutableArray(),
                snapshotExcludedDirectories = publisher is null ? [outputDestination] : new[] { outputDestination, publisher.GetCandidatePath(outputDestination), packageRoot }
                    .Where(path => path is not null).Select(path => path!).Concat(packageRoot is not null && !packageInsideOutput ? [publisher.GetCandidatePath(packageRoot)] : []).ToImmutableArray(),
                outputKind = manifest.outputKind,
                includeDirectories = manifest.includeDirectories.Select(path => NativeBuildManifestLoader.Resolve(path, loaded.artifactRoot)).ToImmutableArray(),
                systemIncludeDirectories = manifest.systemIncludeDirectories.Select(path => NativeBuildManifestLoader.Resolve(path, loaded.artifactRoot)).ToImmutableArray(),
                defines = manifest.defines,
                compilerArguments = manifest.compilerArguments,
                linkerArguments = manifest.linkerArguments,
                libraries = manifest.libraries,
                libraryDirectories = manifest.libraryDirectories
            };
            plan = provider.Plan(construction, cancellation.Token);
            if (plan.providerId != provider.id || plan.target.targetId != target.targetId || plan.target.platformId != target.platformId || plan.target.architectureId != target.architectureId
                || plan.target.abiId != target.abiId || plan.target.triple != target.triple || plan.target.byteOrder != target.byteOrder || plan.outputKind != manifest.outputKind
                || !Path.GetFullPath(plan.outputFile).Equals(Path.GetFullPath(construction.outputFile), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("A native provider changed the frozen provider, target, ABI or owned output contract.");
            if (registration is not null && provider.id != registration.Id) throw new InvalidOperationException("A native provider changed its identity during planning.");
            if (publisher is not null)
                plan = plan with
                {
                    snapshotExcludedDirectories = new[] { outputDestination, publisher.GetCandidatePath(outputDestination), packageRoot }
                    .Where(path => path is not null).Select(path => path!).Concat(packageRoot is not null && !packageInsideOutput ? [publisher.GetCandidatePath(packageRoot)] : []).ToImmutableArray()
                };
            if (dryRun)
            {
                await ReleaseManifestServicesAsync().ConfigureAwait(false);
                await extensionInputs.VerifyAsync(cancellation.Token).ConfigureAwait(false);
                VerifyPluginInputs();
                if (!NativeBuildManifestLoader.Matches(loaded, cancellation.Token)) throw new IOException("Native manifest inputs changed during dry-run planning.");
            }
            else
            {
                NativeEffectiveFactVerifier? factVerifier = manifest.sourceFacts is not null && NativeSourceFactComparer.RequiresVerification(manifest.sourceFacts)
                    ? new(manifest.sourceFacts, manifest.sourceRetainFunctionBodies ?? throw new InvalidDataException("Native source verification omitted its explicit body-retention option."),
                        composition.registry.Frontend, manifest.generationPathBase ?? Path.GetDirectoryName(Path.GetFullPath(request.manifestPath))!, extensionInputs,
                        manifest.generatedDirectories.SelectMany(directory => directory.files.Where(file => file.logicalSourcePath is not null)
                            .Select(file => new KeyValuePair<string, string>(NativeBuildManifestLoader.Resolve(directory.path + "/" + file.relativePath, loaded.artifactRoot), file.logicalSourcePath!)))
                            .ToImmutableDictionary(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
                        manifest.workerPlugins, manifest.workerPluginContext) : null;
                NativeBuildExecutionResult execution = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromSeconds(request.timeoutSeconds), cancellation.Token,
                    factVerifier is null ? null : (
                        operation,
                        token
                    ) => factVerifier.VerifyAsync(plan, operation, token),
                    manifest.factVerificationSources.Select(path => NativeBuildManifestLoader.Resolve(path, loaded.artifactRoot)).ToImmutableArray()).ConfigureAwait(false);
                literalProofs = factVerifier?.literalProofs ?? [];
                if (request.verifyExports)
                {
                    exports = await NativeExportInspector.InspectAsync(target, plan.outputFile, manifest.requiredExports, cancellation.Token, request.exportToolPath, manifest.outputKind).ConfigureAwait(false);
                    if (!exports.success) throw new InvalidOperationException("Native construction omitted required exports: " + string.Join(", ", exports.missing));
                }
                byte[] binary = await File.ReadAllBytesAsync(plan.outputFile, cancellation.Token).ConfigureAwait(false);
                Dictionary<string, ImmutableArray<ArtifactDescriptor>> outputs = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                List<ArtifactDescriptor> outputArtifacts = [];
                string outputCandidate = publisher!.GetCandidatePath(outputDestination);
                string binaryRelative = directoryOutput ? Path.GetRelativePath(outputDestination, final).Replace('\\', '/') : Path.GetFileName(final);
                if (directoryOutput)
                {
                    string receiptRelative = binaryRelative + ".verification.json";
                    ImmutableArray<string> replaced = packageInsideOutput ? [binaryRelative, receiptRelative, Path.GetRelativePath(outputDestination, packageRoot!).Replace('\\', '/') + "/"] : [binaryRelative, receiptRelative];
                    CopyPrevious(publisher.ReadExistingArtifacts(outputDestination), outputDestination, outputCandidate, replaced, outputArtifacts);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(outputCandidate, binaryRelative))!);
                    await File.WriteAllBytesAsync(Path.Combine(outputCandidate, binaryRelative), binary, cancellation.Token).ConfigureAwait(false);
                    outputArtifacts.Add(new(binaryRelative, "application/octet-stream", Digest(binary)));
                    string receipt = JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        targetId = target.targetId.value,
                        nativeLinked = true,
                        invoked = false,
                        plan,
                        exports,
                        inputSnapshot = execution.inputSnapshot,
                        literalProofs
                    }, WorkerProtocol.jsonOptions) + "\n";
                    await File.WriteAllTextAsync(Path.Combine(outputCandidate, receiptRelative), receipt, new UTF8Encoding(false), cancellation.Token).ConfigureAwait(false);
                    outputArtifacts.Add(new(receiptRelative, "application/json", Digest(Encoding.UTF8.GetBytes(receipt))));
                }
                else { await File.WriteAllBytesAsync(outputCandidate, binary, cancellation.Token).ConfigureAwait(false); outputArtifacts.Add(new(binaryRelative, "application/octet-stream", Digest(binary))); }
                if (packageRoot is not null)
                {
                    if (composition.registry.NativeArtifactPackagers.Length > 1) throw new InvalidOperationException("Standalone native packaging requires one unambiguous configured packager.");
                    NativePackagerRegistration? configuredPackager = composition.registry.NativeArtifactPackagers.SingleOrDefault();
                    packager = configuredPackager is null ? new NativeAssetPackager() : configuredPackager.Factory() ?? throw new InvalidOperationException("The native packager factory returned no service.");
                    if (configuredPackager is not null && packager.id != configuredPackager.Id) throw new InvalidOperationException("A native packager changed its frozen identity.");
                    extensionInputs.Capture(packager, target, cancellation.Token);
                    string packageCandidate = packageInsideOutput ? Path.Combine(outputCandidate, Path.GetRelativePath(outputDestination, packageRoot)) : publisher.GetCandidatePath(packageRoot);
                    Directory.CreateDirectory(packageCandidate);
                    List<NativeAssetInput> packageInputs = ReadPackageInputs(packageRoot);
                    packageInputs.RemoveAll(input => input.targetId == target.targetId && Path.GetFileName(input.path) == Path.GetFileName(plan.outputFile));
                    packageInputs.Add(new(plan.outputFile, target.targetId, manifest.outputKind, Digest(binary)) { target = target });
                    NativeAssetPackageResult packaged = await packager.PackageAsync(new(packageInputs.ToImmutableArray(), new DirectoryArtifactSink(packageCandidate)), cancellation.Token).ConfigureAwait(false);
                    packageArtifacts = packaged.assets.Select(asset => new ArtifactDescriptor(asset.relativePath, "application/octet-stream", asset.digest)).Concat(packaged.metadataArtifacts).ToImmutableArray();
                    if (packageInsideOutput)
                    {
                        string prefix = Path.GetRelativePath(outputDestination, packageRoot).Replace('\\', '/').TrimEnd('/') + "/";
                        outputArtifacts.RemoveAll(artifact => artifact.relativePath.StartsWith(prefix, StringComparison.Ordinal));
                        outputArtifacts.AddRange(packageArtifacts.Select(artifact => artifact with { relativePath = prefix + artifact.relativePath }));
                    }
                    else outputs.Add(packageRoot, packageArtifacts);
                }
                outputs.Add(outputDestination, outputArtifacts.ToImmutableArray());
                foreach (ValidatorRegistration gate in composition.registry.Validators)
                {
                    IGenerationValidator validator = gate.Factory() ?? throw new InvalidOperationException("A native validation gate returned no service.");
                    try
                    {
                        if (validator.id != gate.Id || validator.priority != gate.Priority) throw new InvalidOperationException("A native validation gate changed its frozen identity or order.");
                        extensionInputs.Capture(validator, target, cancellation.Token);
                        string readerRoot = directoryOutput ? outputCandidate : Path.GetDirectoryName(outputCandidate)!;
                        IArtifactReader reader = directoryOutput ? new DirectoryArtifactReader(readerRoot, outputArtifacts) : new ExactNativeFileReader(outputCandidate, outputArtifacts[0]);
                        var checks = await validator.ValidateAsync(new(target, manifest.interop!, outputArtifacts.ToImmutableArray(), reader)
                        { managedCompiled = false, nativeLinked = true, nativeArtifactVerified = exports?.success == true }, cancellation.Token).ConfigureAwait(false);
                        if (checks.IsDefault || checks.Any(diagnostic => diagnostic is null || !Enum.IsDefined(diagnostic.severity))) throw new InvalidOperationException("A native validation gate returned an uninitialized or invalid diagnostic collection.");
                        if (validator.id != gate.Id || validator.priority != gate.Priority) throw new InvalidOperationException("A native validation gate changed its frozen identity during validation.");
                        diagnostics.AddRange(checks);
                    }
                    finally
                    {
                        if (validator is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                        else if (validator is IDisposable disposable) disposable.Dispose();
                    }
                }
                if (diagnostics.Any(diagnostic => diagnostic.isFailure)) throw new InvalidOperationException("A configured native validation gate rejected the candidate artifact set.");
                await ReleaseManifestServicesAsync().ConfigureAwait(false);
                await extensionInputs.VerifyAsync(cancellation.Token).ConfigureAwait(false);
                VerifyPluginInputs();
                if (!NativeBuildManifestLoader.Matches(loaded, cancellation.Token) || !SourceSnapshotVerifier.Matches(execution.inputSnapshot, cancellation.Token)
                    || !SourceSnapshotVerifier.Matches(execution.argumentInputs, cancellation.Token))
                    throw new IOException("Native manifest, compiler, headers or linker dependencies changed before publication.");
                publisher.Commit(outputs, cancellation.Token);
                published = true;
                if (publisher.cleanupFailure is not null) diagnostics.Add(new(BindingDiagnosticSeverity.Warning, publisher.cleanupFailure.Message, "BGCS-PUBLICATION-CLEANUP"));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception failure) { diagnostics.Add(new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-NATIVE-MANIFEST-BUILD")); }
        finally
        {
            try { await ReleaseManifestServicesAsync().ConfigureAwait(false); }
            catch (Exception failure) { diagnostics.Add(new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-NATIVE-SERVICE-RELEASE")); }
            try { publisher?.Dispose(); }
            catch (Exception failure) { diagnostics.Add(new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-NATIVE-CANDIDATE-CLEANUP")); }
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            { diagnostics.Add(new(published ? BindingDiagnosticSeverity.Warning : BindingDiagnosticSeverity.Error, failure.Message, "BGCS-NATIVE-SCRATCH-CLEANUP")); }
            finally { lock (m_gate) { if (--m_sessions == 0 && m_closing) m_drained.TrySetResult(); } }
        }
        return new(published, plan, final, exports, packageArtifacts, diagnostics.ToImmutable()) { literalProofs = literalProofs };

        void VerifyPluginInputs()
        {
            foreach (PluginRegistration plugin in definition!.Plugins)
                if (plugin.Manifest is not null) new PluginManifestReader().Verify(plugin.Manifest);
        }

        async ValueTask ReleaseManifestServicesAsync()
        {
            INativeArtifactPackager? releasingPackager = packager; packager = null;
            INativeBuildProvider? releasingProvider = provider; provider = null;
            GenerationComposition? releasingComposition = composition; composition = null;
            List<Exception> failures = [];
            foreach (object? service in new object?[] { releasingPackager, releasingProvider, releasingComposition })
            {
                try { if (service is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false); else if (service is IDisposable disposable) disposable.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count > 0) throw new AggregateException("Native session cleanup failed before publication.", failures);
        }
    }
    private sealed class ExactNativeFileReader(
        string path,
        ArtifactDescriptor artifact
    ) : IArtifactReader
    {
        public async ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(
            string relativePath,
            CancellationToken cancellationToken
        ) {
            if (relativePath != artifact.relativePath) throw new ArgumentException("Only the explicitly named candidate native artifact is available.", nameof(relativePath));
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (Digest(bytes) != artifact.digest) throw new IOException("The native file changed after its candidate identity was declared.");
            return bytes;
        }
        public async ValueTask<string> ReadTextAsync(
            string relativePath,
            CancellationToken cancellationToken
        )
            => new UTF8Encoding(false, true).GetString((await ReadBytesAsync(relativePath, cancellationToken).ConfigureAwait(false)).Span);
    }
    private static void CopyPrevious(
        ImmutableArray<ArtifactDescriptor> previous,
        string root,
        string candidate,
        ImmutableArray<string> replaced,
        List<ArtifactDescriptor> artifacts
    ) {
        foreach (ArtifactDescriptor artifact in previous.Where(artifact => !replaced.Any(path => path == artifact.relativePath || path.EndsWith('/') && artifact.relativePath.StartsWith(path, StringComparison.Ordinal))))
        {
            string destination = Path.Combine(candidate, artifact.relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(root, artifact.relativePath), destination);
            artifacts.Add(artifact);
        }
    }
    private static List<NativeAssetInput> ReadPackageInputs(string packageRoot)
    {
        string manifest = Path.Combine(packageRoot, "bgcs.native-assets.json");
        if (!File.Exists(manifest)) return [];
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(manifest));
        if (json.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("The previous native asset package manifest is unsupported.");
        NativePackageAsset[] assets = JsonSerializer.Deserialize<NativePackageAsset[]>(json.RootElement.GetProperty("assets").GetRawText(), WorkerProtocol.jsonOptions)
            ?? throw new InvalidDataException("The previous native package assets are null.");
        return assets.Select(asset => new NativeAssetInput(NativeBuildManifestLoader.Resolve("artifact:/" + asset.relativePath, packageRoot), asset.targetId, asset.outputKind, asset.digest) { target = asset.target }).ToList();
    }
    private static string RequireAbsolute(
        string path,
        string description
    ) => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : throw new ArgumentException("An explicit " + description + " must be absolute.");
    private static bool Within(
        string path,
        string root
    ) => string.Equals(path, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static ContentDigest Digest(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
}
