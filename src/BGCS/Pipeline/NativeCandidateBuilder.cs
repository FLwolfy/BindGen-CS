using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Native;
using BGCS.NativeBuild.Validation;
using BGCS.Infrastructure.Publishing;
using BGCS.NativeBuild;
using BGCS.NativeBuild.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Composition;

namespace BGCS;

internal sealed class NativeCandidateBuilder
{
    internal sealed record Result(
        ArtifactDescriptor artifact,
        bool linked,
        object evidence,
        SourceSnapshot inputSnapshot
    )
    {
        internal SourceSnapshot argumentInputs { get; init; } = new([], []);
    }

    internal async Task<Result> BuildAsync(
        BindingConfiguration configuration,
        NativeTargetDescriptor target,
        InteropModule interop,
        IEnumerable<string> bridgeSources,
        IEnumerable<string> bridgeIncludeDirectories,
        ImmutableArray<string> declaredToolchainDefines,
        string baseDirectory,
        DirectoryArtifactSink sink,
        string relativeDirectory,
        CancellationToken cancellationToken,
        Func<INativeBuildProvider>? customPlannerFactory = null,
        ImmutableArray<string> snapshotExcludedDirectories = default,
        GenerationInputTracker? inputTracker = null,
        ImmutableArray<string> requiredExports = default,
        NativeModule? sourceFacts = null,
        Func<IFrontend>? proofFrontendFactory = null,
        ImmutableArray<string> factVerificationSources = default,
        ImmutableDictionary<string, string>? sourcePathMappings = null,
        FrontendRequest? preparedFrontend = null,
        NativeRecordTraitOrigins? traitOrigins = null
    ) {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-native-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            bool linked = configuration.nativeBuild.mode == "sources";
            NativeOutputKind outputKind = configuration.nativeBuild.outputKind ?? BGCS.Platforms.Capabilities.PlatformCapabilityCatalog.Get(target).defaultOutputKind;
            string fileName = BGCS.Platforms.Assets.NativeOutputLayout.ResolveFilename(configuration.library.nativeName, target, outputKind);
            string binary = linked ? Path.Combine(temporary, fileName) : Path.GetFullPath(configuration.nativeBuild.artifacts.Single(), baseDirectory);
            object? construction = null;
            SourceSnapshot inputSnapshot = new([], []);
            SourceSnapshot argumentInputs = new([], []);
            if (!linked)
            {
                string original = binary;
                byte[] snapshot = await File.ReadAllBytesAsync(original, cancellationToken).ConfigureAwait(false);
                inputSnapshot = new([new(original, BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(original), snapshot.LongLength,
                    new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(snapshot))))], []);
                binary = Path.Combine(temporary, fileName);
                await File.WriteAllBytesAsync(binary, snapshot, cancellationToken).ConfigureAwait(false);
            }
            if (linked)
            {
                ImmutableArray<NativeCompilationUnit> units = configuration.nativeBuild.sources.Select(path =>
                {
                    string absolute = Path.GetFullPath(path, baseDirectory);
                    SourceLanguage language = Path.GetExtension(path) == ".c" ? SourceLanguage.C : SourceLanguage.Cpp;
                    return new NativeCompilationUnit(absolute, language, language == SourceLanguage.C ? configuration.source.language == SourceLanguage.C ? configuration.source.languageStandard! : "c17"
                        : configuration.source.language == SourceLanguage.Cpp ? configuration.source.languageStandard! : "c++23");
                }).Concat(bridgeSources.Select(path => new NativeCompilationUnit(path, Path.GetExtension(path) == ".c" ? SourceLanguage.C : SourceLanguage.Cpp,
                    Path.GetExtension(path) == ".c" ? configuration.source.language == SourceLanguage.C ? configuration.source.languageStandard! : "c17" : configuration.source.languageStandard!))).ToImmutableArray();
                var provider = customPlannerFactory is null ? new NativeBuildProviderResolver().Resolve(target, configuration.nativeBuild.provider,
                    ResolveTool(configuration.nativeBuild.buildToolPath), ResolveTool(configuration.nativeBuild.auxiliaryToolPath))
                    : customPlannerFactory() ?? throw new InvalidOperationException("A custom native construction provider factory returned no service.");
                try
                {
                    inputTracker?.Capture(provider, target, cancellationToken);
                    if (customPlannerFactory is not null && provider.id != configuration.nativeBuild.provider)
                        throw new InvalidOperationException("A custom native provider changed its frozen registered identity.");
                    NativeBuildRequest request = new(target, units, temporary, binary)
                    {
                        argumentWorkingDirectory = baseDirectory,
                        preprocessingSources = factVerificationSources.IsDefault ? [] : factVerificationSources.Select(path => new NativeCompilationUnit(path,
                            sourceFacts?.language ?? configuration.source.language, configuration.source.languageStandard!)).ToImmutableArray(),
                        snapshotExcludedDirectories = snapshotExcludedDirectories.IsDefault ? [] : snapshotExcludedDirectories,
                        outputKind = outputKind,
                        includeDirectories = configuration.source.includeDirectories.Select(path => Path.GetFullPath(path, baseDirectory)).Concat(bridgeIncludeDirectories).Distinct(StringComparer.Ordinal).ToImmutableArray(),
                        systemIncludeDirectories = configuration.source.systemIncludeDirectories.Select(path => Path.GetFullPath(path, baseDirectory))
                            .Concat(target.toolchain.systemIncludeFolders).Concat(target.toolchain.cxxSystemIncludeFolders).Distinct(StringComparer.Ordinal).ToImmutableArray(),
                        defines = configuration.source.defines.AddRange(declaredToolchainDefines),
                        compilerArguments = configuration.nativeBuild.compilerArguments,
                        linkerArguments = configuration.nativeBuild.linkerArguments,
                        libraries = configuration.nativeBuild.libraries.Select(library => library.Contains('/') || library.Contains('\\') || Path.HasExtension(library) ? Path.GetFullPath(library, baseDirectory) : library).ToImmutableArray(),
                        libraryDirectories = configuration.nativeBuild.libraryDirectories.Select(path => Path.GetFullPath(path, baseDirectory)).ToImmutableArray()
                    };
                    var plan = provider.Plan(request, cancellationToken) with { snapshotExcludedDirectories = snapshotExcludedDirectories.IsDefault ? [] : snapshotExcludedDirectories };
                    if (plan.providerId != provider.id || plan.target.targetId != target.targetId || plan.target.platformId != target.platformId || plan.target.architectureId != target.architectureId
                        || plan.target.abiId != target.abiId || plan.target.triple != target.triple || plan.target.byteOrder != target.byteOrder || plan.outputKind != outputKind
                        || !Path.GetFullPath(plan.outputFile).Equals(Path.GetFullPath(binary), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException("A native provider changed the requested provider, target or owned output contract.");
                    NativeEffectiveFactVerifier? verifier = sourceFacts is not null && NativeSourceFactComparer.RequiresVerification(sourceFacts)
                        ? new(sourceFacts, preparedFrontend?.retainFunctionBodies ?? throw new InvalidOperationException("Native source verification requires its exact prepared frontend body-retention option."),
                            proofFrontendFactory ?? throw new InvalidOperationException("Generated native record contracts require an independent effective-source frontend."),
                            baseDirectory, inputTracker, sourcePathMappings, preparedFrontend?.workerPlugins ?? [], preparedFrontend?.workerPluginContext,
                            traitOrigins: traitOrigins) : null;
                    NativeBuildExecutionResult executed = await new PlannedNativeBuildExecutor().ExecuteWithDependenciesAsync(plan, TimeSpan.FromSeconds(configuration.execution.nativeBuildTimeoutSeconds), cancellationToken,
                        verifier is null ? null : (
                            operation,
                            token
                        ) => verifier.VerifyAsync(plan, operation, token), factVerificationSources).ConfigureAwait(false);
                    argumentInputs = executed.argumentInputs;
                    inputSnapshot = executed.inputSnapshot with
                    {
                        files = executed.inputSnapshot.files.Where(input => !IsPrivateBuildPath(input.path)).ToImmutableArray(),
                        searchDirectories = executed.inputSnapshot.searchDirectories.Where(input => !IsPrivateBuildPath(input.path)).ToImmutableArray()
                    };
                    construction = new { plan, executions = executed.executions, literalProofs = verifier?.literalProofs ?? [] };
                }
                finally
                {
                    if (provider is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                    else if (provider is IDisposable disposable) disposable.Dispose();
                }
            }
            else if (configuration.nativeBuild.prebuiltTargetId != target.targetId.value)
                throw new InvalidOperationException("A prebuilt artifact must explicitly declare the same concrete native target; file architecture alone does not prove its libc or ABI.");
            var exports = await NativeExportInspector.InspectAsync(target, binary, requiredExports.IsDefault ? interop.functions.Select(function => function.exportName) : requiredExports, cancellationToken, outputKind: outputKind).ConfigureAwait(false);
            if (!exports.success) throw new InvalidOperationException("The native artifact is missing required exports: " + string.Join(", ", exports.missing));
            byte[] content = await File.ReadAllBytesAsync(binary, cancellationToken).ConfigureAwait(false);
            string relative = relativeDirectory + "/" + fileName;
            sink.WriteBytes(relative, content);
            ArtifactDescriptor artifact = new(relative, "application/octet-stream", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(content))));
            return new(artifact, linked, new { target = target.targetId.value, targetIdentityEvidence = linked ? "buildPlan" : "userAsserted", exports, construction }, inputSnapshot) { argumentInputs = argumentInputs };
        }
        finally { Directory.Delete(temporary, recursive: true); }

        string? ResolveTool(string? path) => path is not null && (Path.IsPathRooted(path) || path.Contains('/') || path.Contains('\\')) ? Path.GetFullPath(path, baseDirectory) : path;
        bool IsPrivateBuildPath(string path)
        {
            string physical = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(path);
            string root = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(temporary);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return physical.Equals(root, comparison) || physical.StartsWith(root + Path.DirectorySeparatorChar, comparison);
        }
    }
}
