using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Composition;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Plugins;
using BGCS.Infrastructure.IO;
using BGCS.NativeBuild.Planning;
using BGCS.NativeBuild.Validation;
using BGCS.Platforms.Targeting;

namespace BGCS;

internal sealed class NativeEffectiveFactVerifier(
    NativeModule sourceFacts,
    bool retainFunctionBodies,
    Func<IFrontend> frontendFactory,
    string pathBase,
    GenerationInputTracker? inputTracker,
    ImmutableDictionary<string, string>? sourcePathMappings = null,
    ImmutableArray<WorkerPluginActivation> workerPlugins = default,
    CacheFingerprintContext? workerPluginContext = null,
    Action<SourceSnapshot>? captureVerificationInputs = null,
    NativeRecordTraitOrigins? traitOrigins = null
)
{
    private readonly ImmutableArray<NativeStringProofReceipt>.Builder m_literalProofs = ImmutableArray.CreateBuilder<NativeStringProofReceipt>();
    internal ImmutableArray<NativeStringProofReceipt> literalProofs => m_literalProofs.ToImmutable();

    internal async ValueTask VerifyAsync(
        NativeBuildPlan plan,
        NativePreprocessingOperation operation,
        CancellationToken cancellationToken,
        string? ownedProofRoot = null
    ) {
        if (operation.language != sourceFacts.language) throw new InvalidOperationException("Effective native fact verification changed the selected source language.");
        NativeTargetDescriptor target = plan.target;
        ImmutableArray<string> arguments = NativeEffectiveFactArguments.Resolve(plan, operation, cancellationToken);
        var effectiveSource = await NativeEffectiveSourceAdapter.PrepareAsync(plan, operation, cancellationToken).ConfigureAwait(false);
        bool observeTraits = (plan.compilerFamily ?? plan.providerId) == "gnu" && operation.language == SourceLanguage.Cpp
            && effectiveSource.declarationPrelude is not null;
        ContentDigest? preprocessedDigest = null;
        if (observeTraits)
        {
            using FileStream raw = File.OpenRead(operation.outputFile);
            preprocessedDigest = new(Convert.ToHexStringLower(await SHA256.HashDataAsync(raw, cancellationToken).ConfigureAwait(false)));
        }
        string? compilerPath = target.toolchain.compilerPath;
        if (effectiveSource.declarationPrelude is not null)
        {
            // A provider can select a different concrete driver from its target
            // descriptor. Preserve that driver's SDK order for include_next.
            compilerPath = plan.compilerPath ?? operation.command.executable;
            if (!Path.IsPathFullyQualified(compilerPath) || !File.Exists(compilerPath))
                throw new InvalidOperationException("An effective declaration prelude requires the selected absolute native compiler path.");
        }
        NativeToolchainDescriptor toolchain = new(compilerPath, target.toolchain.sysRoot,
            target.toolchain.systemIncludeFolders, [], [], target.toolchain.cxxSystemIncludeFolders,
            target.toolchain.sdkVersion, target.toolchain.compilerVersion, target.toolchain.linkerPath, target.toolchain.libraryDirectories);
        NativeTargetDescriptor parserTarget = new(target.targetId, target.platformId, target.architectureId, target.abiId,
            target.triple, toolchain, target.capabilities, target.byteOrder);
        ImmutableArray<string> exclusions = plan.snapshotExcludedDirectories.AddRange(plan.scratchDirectory is string scratch ? ImmutableArray.Create(scratch) : ImmutableArray<string>.Empty);
        FrontendRequest request = new(parserTarget, operation.language, [effectiveSource.sourceFile])
        {
            purpose = FrontendRequestPurpose.NativeBuildVerification,
            languageStandard = operation.languageStandard,
            arguments = arguments,
            pathBase = pathBase,
            preamble = effectiveSource.preamble,
            declarationPrelude = effectiveSource.declarationPrelude,
            // Match the adapter's actual driver query, including cross drivers.
            headerSearchPaths = effectiveSource.declarationPrelude is null ? []
                : ToolchainDiscovery.DiscoverHeaderSearchPaths(operation.language, compilerPath, toolchain.sysRoot).ToImmutableArray(),
            sourcePathMappings = sourcePathMappings ?? ImmutableDictionary<string, string>.Empty,
            workerPlugins = workerPlugins.IsDefault ? [] : workerPlugins,
            workerPluginContext = workerPluginContext is null ? null : new(workerPluginContext.configuration, workerPluginContext.pathBase, parserTarget),
            parseMacros = false,
            parseComments = false,
            parseSystemIncludes = false,
            snapshotExcludedDirectories = exclusions,
            retainFunctionBodies = retainFunctionBodies
        };
        CacheFingerprintContext? context = request.workerPluginContext;
        if (!request.workerPlugins.IsEmpty && context is null) throw new InvalidDataException("Effective native worker contributors require their frozen fingerprint context.");
        GenerationInputTracker? tracked = inputTracker ?? (context is null ? null : new(context.configuration, context.pathBase));
        IFrontend frontend = frontendFactory() ?? throw new InvalidOperationException("The effective-source frontend factory returned no service.");
        try
        {
            tracked?.Capture(frontend, target, cancellationToken);
            request = await WorkerPluginPreparation.PrepareActivationsAsync(request, frontend, tracked, cancellationToken).ConfigureAwait(false);
            FrontendResult parsed = await frontend.ParseAsync(request, cancellationToken).ConfigureAwait(false);
            if (parsed.snapshot.excludedDirectories.Any(directory => !exclusions.Contains(directory, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)))
                throw new InvalidOperationException("The effective-source frontend excluded an undeclared search directory.");
            if (!SourceSnapshotVerifier.Matches(parsed.snapshot, cancellationToken)) throw new IOException("Effective native source inputs changed during fact parsing.");
            NativeModule observedModule = parsed.module;
            if (observeTraits)
            {
                string traitScratch = plan.scratchDirectory
                    ?? throw new InvalidDataException("The GNU effective-source record-trait observation omitted its native working directory.");
                NativeRecordTraitOrigins.Capture? origins = traitOrigins?.Authenticate(plan, ownedProofRoot,
                    sourcePathMappings ?? ImmutableDictionary<string, string>.Empty, cancellationToken);
                NativeRecordTraitObservation observation = await new NativeRecordTraitObserver().ObserveAsync(plan, operation, preprocessedDigest!.Value,
                    parsed.module, Path.Combine(PhysicalPathResolver.Resolve(traitScratch), "record-traits-" + Guid.NewGuid().ToString("N")), TimeSpan.FromSeconds(300), cancellationToken,
                    sourcePathMappings: origins?.observer ?? sourcePathMappings).ConfigureAwait(false);
                observedModule = NativeRecordTraitApplication.Apply(parsed.module, observation);
                tracked ??= new(JsonSerializer.SerializeToElement(new { plan.target, purpose = "nativeRecordTraits" }), pathBase);
                tracked.CaptureNativeRecordTraits(target.targetId.value, observation, traitScratch,
                    origins?.files ?? sourcePathMappings ?? ImmutableDictionary<string, string>.Empty,
                    ownedDirectoryRoles: origins?.directories);
                if (captureVerificationInputs is not null)
                    foreach (SourceSnapshot snapshot in observation.inputs) captureVerificationInputs(snapshot);
            }
            new NativeSourceFactComparer().Verify(sourceFacts, observedModule, cancellationToken);
            NativeStringProofReceipt? literalProof = await new NativeStringCompilerVerifier().VerifyAsync(plan, operation, sourceFacts, observedModule, cancellationToken).ConfigureAwait(false);
            if (literalProof is not null) m_literalProofs.Add(literalProof);
            SourceSnapshot retained = parsed.snapshot with
            {
                files = parsed.snapshot.files.Where(input => !Private(input.path)).ToImmutableArray(),
                searchDirectories = parsed.snapshot.searchDirectories.Where(input => !Private(input.path)).ToImmutableArray(),
                absentInputs = parsed.snapshot.absentInputs.Where(input => !Private(input.path)).ToImmutableArray()
            };
            CaptureInputs(retained);
            if (parsed.executionInputs is SourceSnapshot execution)
            {
                if (!SourceSnapshotVerifier.Matches(execution, cancellationToken)) throw new IOException("Effective-source parser execution resources changed during verification.");
                CaptureInputs(execution);
            }
            if (inputTracker is null && tracked is not null) await tracked.VerifyAsync(cancellationToken).ConfigureAwait(false);

            void CaptureInputs(SourceSnapshot snapshot)
            {
                if (captureVerificationInputs is not null) captureVerificationInputs(snapshot);
                else tracked?.CaptureArgumentInputs(snapshot);
            }
        }
        finally
        {
            if (frontend is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
            else if (frontend is IDisposable disposable) disposable.Dispose();
        }

        bool Private(string path)
        {
            if (plan.scratchDirectory is not string root) return false;
            string physical = PhysicalPathResolver.Resolve(path), parent = PhysicalPathResolver.Resolve(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return physical.Equals(parent, comparison) || physical.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
        }
    }
}
