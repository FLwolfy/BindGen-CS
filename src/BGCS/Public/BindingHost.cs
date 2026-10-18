using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BGCS.Analysis.Interop;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Native;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using BGCS.Composition;
using BGCS.Infrastructure.IO;

namespace BGCS;

/// <summary>Owns isolated native-analysis sessions and freezes service composition at construction.</summary>
public sealed partial class BindingHost : IAsyncDisposable
{
    private readonly FrozenExtensionRegistry m_registry;
    private readonly object m_gate = new();
    private readonly CancellationTokenSource m_shutdown = new();
    private readonly TaskCompletionSource m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool m_closing;
    private int m_sessions;
    private int m_shutdownDisposed;

    internal BindingHost(FrozenExtensionRegistry registry) => m_registry = registry;

    internal sealed record NativeTransformRegistration(
        string Id,
        int Priority,
        Func<INativeModuleTransform> Factory
    );
    internal sealed record InteropTransformRegistration(
        string Id,
        int Priority,
        Func<IInteropTransform> Factory
    );
    internal sealed record ManagedTransformRegistration(
        string Id,
        int Priority,
        Func<IManagedApiTransform> Factory
    );
    internal sealed record CppTypeRegistration(
        string Id,
        int Priority,
        Func<ICppTypeLowering> Factory
    );
    internal sealed record CppCallableRegistration(
        string Id,
        int Priority,
        Func<ICppCallableLowering> Factory
    );

    /// <summary>Creates the default host with isolated worker execution.</summary>
    /// <returns>An independently owned host.</returns>
    public static BindingHost CreateDefault() => CreateBuilder().AddBuiltIns().Build();

    /// <summary>Creates a composition builder for one host.</summary>
    /// <returns>A new builder.</returns>
    public static BindingHostBuilder CreateBuilder() => new();

    /// <summary>Resolves source inputs, runs the frontend and checks the detached native graph.</summary>
    /// <param name="request">The immutable source-analysis request.</param>
    /// <param name="cancellationToken">Cancels target discovery or frontend execution.</param>
    /// <returns>Frozen facts and diagnostics with no live parser handles.</returns>
    public Task<AnalysisResult> AnalyzeAsync(
        AnalysisRequest request,
        CancellationToken cancellationToken = default
    )
        => AnalyzeCoreAsync(request, applyTransforms: true, cancellationToken);

    private async Task<AnalysisResult> AnalyzeCoreAsync(
        AnalysisRequest request,
        bool applyTransforms,
        CancellationToken cancellationToken,
        GenerationComposition? generationComposition = null,
        GenerationInputTracker? inputTracker = null,
        FrontendGenerationCache? frontendCache = null,
        Func<FrontendRequest, CancellationToken, ValueTask<FrontendRequest>>? prepareFrontend = null
    ) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            m_sessions++;
        }
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
        IFrontend? frontend = null;
        List<INativeModuleTransform> transforms = [];
        List<INativeTargetProvider> providers = [];
        GenerationComposition? ownedComposition = null;
        AnalysisResult? result = null;
        ExceptionDispatchInfo? cancellationFailure = null;
        List<BindingDiagnostic> releaseFailures = [];
        GenerationInputTracker? ownedInputs = null;
        try
        {
            if (inputTracker is null) inputTracker = ownedInputs = new(JsonSerializer.SerializeToElement(request), Path.GetFullPath(request.baseDirectory));
            GenerationInputTracker activeInputs = inputTracker;
            ownedComposition = generationComposition is null ? await GenerationComposition.CreateAsync(m_registry, cancellation.Token, activeInputs).ConfigureAwait(false) : null;
            FrozenExtensionRegistry registry = (generationComposition ?? ownedComposition!).registry;
            if (request.headers.IsDefaultOrEmpty || request.language == SourceLanguage.None)
                throw new ArgumentException("A source language and at least one entry header are required.", nameof(request));
            string pathBase = Path.GetFullPath(request.baseDirectory);
            string? compiler = request.toolchain.compilerPath;
            NativeToolchainDescriptor toolchain = new(
                compilerPath: compiler is not null && (Path.IsPathRooted(compiler) || compiler.Contains('/') || compiler.Contains('\\')) ? Path.GetFullPath(compiler, pathBase) : compiler,
                sysRoot: request.toolchain.sysRoot is null ? null : Path.GetFullPath(request.toolchain.sysRoot, pathBase),
                systemIncludeFolders: request.toolchain.systemIncludeFolders.Select(path => Path.GetFullPath(path, pathBase)),
                cxxSystemIncludeFolders: request.toolchain.cxxSystemIncludeFolders.Select(path => Path.GetFullPath(path, pathBase)),
                defines: request.toolchain.defines, arguments: request.toolchain.arguments,
                sdkVersion: request.toolchain.sdkVersion, compilerVersion: request.toolchain.compilerVersion);
            foreach (TargetProviderRegistration registration in registry.Targets)
            {
                INativeTargetProvider provider = registration.Factory() ?? throw new InvalidOperationException($"Target provider '{registration.Id}' returned no service.");
                providers.Add(provider);
                activeInputs.CaptureDeclared(provider, cancellation.Token);
            }
            NativeTargetDescriptor target = new TargetResolver(TargetResolver.CreateBuiltInProviders().Concat(providers)).Resolve(new(new NativeTargetId(request.targetId), toolchain, request.triple));
            target = ToolchainResolver.ResolveToolchain(target, request.language);
            (generationComposition ?? ownedComposition!).CaptureFingerprints(target, activeInputs, cancellation.Token);
            foreach (INativeTargetProvider provider in providers) activeInputs.Capture(provider, target, cancellation.Token);
            FrontendRequest resolved = new(target, request.language, request.headers.Select(path => Path.GetFullPath(path, pathBase)).ToImmutableArray())
            {
                purpose = applyTransforms ? FrontendRequestPurpose.SourceAnalysis : FrontendRequestPurpose.BridgeVerification,
                usePresumedSourceLocations = true,
                pathBase = pathBase,
                languageStandard = request.languageStandard,
                includeDirectories = request.includeDirectories.Select(path => Path.GetFullPath(path, pathBase)).ToImmutableArray(),
                systemIncludeDirectories = request.systemIncludeDirectories.Select(path => Path.GetFullPath(path, pathBase)).ToImmutableArray(),
                headerSearchPaths = ToolchainResolver.ResolveHeaderSearchPaths(target, request.language).ToImmutableArray(),
                defines = request.defines.ToArray().ToImmutableArray(),
                arguments = request.arguments.ToArray().ToImmutableArray(),
                preamble = request.preamble,
                postamble = request.postamble,
                parseMacros = request.parseMacros,
                parseComments = request.parseComments,
                parseSystemIncludes = request.parseSystemIncludes,
                retainFunctionBodies = request.retainFunctionBodies,
                snapshotExcludedDirectories = request.snapshotExcludedDirectories,
                sourcePathMappings = request.sourcePathMappings
            };
            cancellation.Token.ThrowIfCancellationRequested();
            NativeArgumentExpansionResult expanded = new NativeArgumentInputSnapshotBuilder().Expand(
                target.toolchain.arguments.Concat(resolved.arguments), pathBase, windowsResponseSyntax: OperatingSystem.IsWindows(),
                clangConfigurations: true, compilerPath: target.toolchain.compilerPath, cancellationToken: cancellation.Token,
                snapshotExcludedDirectories: resolved.snapshotExcludedDirectories,
                includeSearchDirectories: resolved.includeDirectories.Concat(resolved.systemIncludeDirectories)
                    .Concat(target.toolchain.cxxSystemIncludeFolders).Concat(target.toolchain.systemIncludeFolders)
                    .Concat(resolved.headerSearchPaths.Select(entry => entry.path)), sysRoot: target.toolchain.sysRoot);
            NativeArgumentIdentityValidator.Validate(expanded.arguments.Concat(expanded.linkerOnlyArguments), "frontend", rejectParserSubstitution: true);
            SourceSnapshot argumentInputs = expanded.snapshot;
            activeInputs.CaptureArgumentInputs(argumentInputs);
            activeInputs.DisableCaching(expanded.cacheIneligibilityReasons);
            // The original descriptor remains the analysis/build provenance. The worker
            // receives exactly one expanded argument vector, without reopening its files.
            NativeToolchainDescriptor parserToolchain = new(target.toolchain.compilerPath, target.toolchain.sysRoot,
                target.toolchain.systemIncludeFolders, target.toolchain.defines, [], target.toolchain.cxxSystemIncludeFolders,
                target.toolchain.sdkVersion, target.toolchain.compilerVersion);
            NativeTargetDescriptor parserTarget = new(target.targetId, target.platformId, target.architectureId, target.abiId,
                target.triple, parserToolchain, target.capabilities, target.byteOrder);
            resolved = resolved with { target = parserTarget, arguments = expanded.arguments };
            if (prepareFrontend is not null)
                resolved = await prepareFrontend(resolved, cancellation.Token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Generated source preparation returned no frozen frontend request.");
            frontend = registry.Frontend() ?? throw new InvalidOperationException("The frontend factory returned no service.");
            activeInputs.Capture(frontend, target, cancellation.Token);
            resolved = await WorkerPluginPreparation.PrepareAsync(resolved, frontend, registry.WorkerPlugins, activeInputs, cancellation.Token).ConfigureAwait(false);
            FrontendGenerationCache.Attempt? cached = frontendCache?.Prepare(resolved, frontend, cancellation.Token);
            FrontendResult parsed = cached?.result ?? await frontend.ParseAsync(resolved, cancellation.Token).ConfigureAwait(false);
            if (parsed.snapshot.excludedDirectories.Any(path => !resolved.snapshotExcludedDirectories.Contains(path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)))
                throw new InvalidOperationException("The frontend excluded an undeclared source search directory.");
            if (BGCS.Infrastructure.IO.SourceSnapshotVerifier.GetMismatch(parsed.snapshot, cancellation.Token) is string nativeInputMismatch)
                throw new IOException("Native inputs changed before their detached facts reached the host. " + nativeInputMismatch);
            if (!BGCS.Infrastructure.IO.SourceSnapshotVerifier.Matches(argumentInputs, cancellation.Token))
                throw new IOException("Native parser response or configuration inputs changed during parsing.");
            if (parsed.executionInputs is not null)
            {
                if (!SourceSnapshotVerifier.Matches(parsed.executionInputs, cancellation.Token))
                    throw new IOException("Detached parser execution resources changed during native analysis.");
                activeInputs.CaptureArgumentInputs(parsed.executionInputs);
            }
            NativeModule module = parsed.module;
            if (module.targetId != target.targetId.value || module.triple != target.triple || module.language != request.language)
                throw new InvalidOperationException("The frontend result changed the resolved native target or source-language provenance.");
            NativeModuleVerifier verifier = new();
            ImmutableArray<BindingDiagnostic> diagnostics = module.diagnostics.AddRange(verifier.Validate(module));
            if (!diagnostics.Any(diagnostic => diagnostic.isFailure) && cached is not null)
                frontendCache!.Capture(cached, resolved, parsed, cancellation.Token);
            if (diagnostics.Any(diagnostic => diagnostic.isFailure))
                result = new(module, parsed.snapshot, diagnostics);
            AnalysisContext context = new(target, request.language, parsed.snapshot);
            foreach (NativeTransformRegistration registration in result is null && applyTransforms ? registry.NativeTransforms : [])
            {
                cancellation.Token.ThrowIfCancellationRequested();
                INativeModuleTransform transform = registration.Factory() ?? throw new InvalidOperationException($"Transformation '{registration.Id}' has no service.");
                transforms.Add(transform);
                activeInputs.Capture(transform, target, cancellation.Token);
                if (transform.id != registration.Id || transform.priority != registration.Priority)
                    throw new InvalidOperationException($"Transformation '{registration.Id}' changed its registered identity or priority.");
                module = transform.Transform(module, context, cancellation.Token) ?? throw new InvalidOperationException($"Transformation '{registration.Id}' returned no module.");
                if (transform.id != registration.Id || transform.priority != registration.Priority)
                    throw new InvalidOperationException($"Transformation '{registration.Id}' changed its frozen identity during execution.");
                if (module.targetId != target.targetId.value || module.triple != target.triple || module.pointerSize != parsed.module.pointerSize || module.language != request.language || module.byteOrder != parsed.module.byteOrder)
                    throw new InvalidOperationException($"Transformation '{registration.Id}' changed the resolved native target.");
                diagnostics = module.diagnostics.AddRange(verifier.Validate(module));
                if (diagnostics.Any(diagnostic => diagnostic.isFailure))
                    break;
            }
            result ??= new(module, parsed.snapshot, diagnostics);
            result = result with
            {
                target = target,
                argumentInputs = argumentInputs,
                preparedFrontend = resolved
            };
        }
        catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
        {
            cancellationFailure = ExceptionDispatchInfo.Capture(exception);
        }
        catch (Exception exception)
        {
            result = new(null, null, [new(BindingDiagnosticSeverity.Error, exception.Message, "BGCS-ANALYSIS-SESSION")]);
        }
        finally
        {
            try
            {
                for (int index = transforms.Count - 1; index >= 0; index--)
                    await ReleaseAsync(transforms[index]).ConfigureAwait(false);
                if (frontend is not null)
                    await ReleaseAsync(frontend).ConfigureAwait(false);
                for (int index = providers.Count - 1; index >= 0; index--) await ReleaseAsync(providers[index]).ConfigureAwait(false);
                if (ownedComposition is not null) await ReleaseAsync(ownedComposition).ConfigureAwait(false);
            }
            finally
            {
                lock (m_gate)
                    if (--m_sessions == 0 && m_closing)
                        m_drained.TrySetResult();
            }
        }
        cancellationFailure?.Throw();
        result ??= new(null, null, []);
        try { if (ownedInputs is not null) await ownedInputs.VerifyAsync(cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure) { releaseFailures.Add(new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-ANALYSIS-INPUTS")); }
        return result with { diagnostics = result.diagnostics.AddRange(releaseFailures) };

        async ValueTask ReleaseAsync(object service)
        {
            try
            {
                if (service is IAsyncDisposable asynchronous)
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                else if (service is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception exception)
            {
                releaseFailures.Add(new(BindingDiagnosticSeverity.Error, $"Session service '{service.GetType().FullName}' failed to release: {exception.Message}", "BGCS-SESSION-RELEASE"));
            }
        }
    }

    /// <summary>Cancels active frontend sessions and waits for all session-owned services to release.</summary>
    /// <returns>The asynchronous host shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        bool cancel;
        lock (m_gate)
        {
            cancel = !m_closing;
            m_closing = true;
            if (m_sessions == 0)
                m_drained.TrySetResult();
        }
        if (cancel)
            m_shutdown.Cancel();
        await m_drained.Task.ConfigureAwait(false);
        if (Interlocked.Exchange(ref m_shutdownDisposed, 1) == 0)
            m_shutdown.Dispose();
    }
}
