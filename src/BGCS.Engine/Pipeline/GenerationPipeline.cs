using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Declarations;
using BGCS.Core.Bridge;
using BGCS.Core.Calls;
using BGCS.Core.Capabilities;
using BGCS.Core.Plugins;
using BGCS.NativeBridge.Planning;
using BGCS.NativeBridge.Emission;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Infrastructure.Publishing;
using BGCS.Composition;
using BGCS.Infrastructure.Caching;
using BGCS.Infrastructure.IO;

namespace BGCS;

/// <summary>Executes frozen generation requests through the neutral model and candidate publication stages.</summary>
public sealed partial class BindingHost
{
    /// <summary>Runs isolated analysis, neutral C ABI projection and candidate publication for all declared targets.</summary>
    /// <param name="request">The frozen new-format request and captured path base.</param>
    /// <param name="cancellationToken">Cancels session execution before publication begins.</param>
    /// <returns>Published artifacts and accurate completed-stage diagnostics.</returns>
    public Task<GenerationResult> GenerateAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default
    )
        => ExecuteGenerationAsync(request, cancellationToken);

    /// <summary>Generates and verifies native C/C++ bridge source without activating managed execution providers.</summary>
    /// <param name="request">The grouped native source generation request.</param>
    /// <param name="cancellationToken">Cancels analysis and candidate publication.</param>
    /// <returns>The jointly published native bridge and metadata without managed emission.</returns>
    public Task<GenerationResult> BridgeAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteGenerationAsync(request, cancellationToken, bridgeOnly: true);
    }

    private async Task<GenerationResult> ExecuteGenerationAsync(
        GenerationRequest request,
        CancellationToken cancellationToken,
        bool bridgeOnly = false
    ) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BindingConfiguration configuration = new();
        string logicalOutput = string.Empty;
        string output = string.Empty;
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            m_sessions++;
        }
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        HashSet<string> diagnosticErrorCodes = new(StringComparer.Ordinal);
        ImmutableArray<ArtifactDescriptor>.Builder artifacts = ImmutableArray.CreateBuilder<ArtifactDescriptor>();
        ImmutableArray<TargetInspection>.Builder targetModels = ImmutableArray.CreateBuilder<TargetInspection>();
        GenerationInputTracker extensionInputs = null!;
        List<GenerationCacheStageResult> cacheEvidence = [];
        List<(GenerationStageCache Cache, ContentDigest Key, ImmutableArray<CachedStageArtifact> Artifacts)> pendingCacheEntries = [];
        List<IArtifactProcessor> pendingProcessorRelease = [];
        List<SourceContributionCapture> sourceContributions = [];
        List<NativeSourceManifest> nativeManifests = [];
        List<NativeInputRootBinding> nativeInputRoots = [];
        List<BindingCapabilityReport> capabilities = [];
        bool published = false;
        GenerationComposition? composition = null;
        try
        {
            composition = await GenerationComposition.CreateHostAsync(m_registry, request.baseDirectory).ConfigureAwait(false);
            var paths = composition.hostServices.paths;
            request = await request.MaterializeAsync(paths, cancellation.Token).ConfigureAwait(false);
            configuration = request.configuration;
            logicalOutput = Path.GetFullPath(configuration.output.root, request.baseDirectory);
            output = request.previewOutputRoot ?? logicalOutput;
            diagnosticErrorCodes = configuration.execution.diagnostics.errorCodes.ToHashSet(StringComparer.Ordinal);
            cancellation.CancelAfter(TimeSpan.FromSeconds(configuration.execution.sessionTimeoutSeconds));
            extensionInputs = new(JsonSerializer.SerializeToElement(configuration), request.baseDirectory);
            extensionInputs.BindHost(paths);
            extensionInputs.CaptureArgumentInputs(request.configurationSnapshot);
            foreach (SourceSnapshot input in request.pluginSnapshots) extensionInputs.CaptureArgumentInputs(input);
            FrozenExtensionRegistry definition = m_registry;
            if (request.plugins.Length != 0 || configuration.extensions.options.Count != 0)
            {
                BindingHostBuilder requested = BindingHostBuilder.FromFrozen(m_registry, includePlugins: true);
                foreach (var plugin in request.plugins)
                {
                    configuration.extensions.options.TryGetValue(plugin.manifest.plugin.id, out PluginSettingsConfiguration? settings);
                    requested.AddPluginManifest(plugin, settings?.values, settings?.pathBase is string pathBase ? Path.GetFullPath(pathBase, request.baseDirectory) : request.baseDirectory);
                }
                foreach ((string id, PluginSettingsConfiguration settings) in configuration.extensions.options)
                    requested.WithPluginSettings(id, settings.values, settings.pathBase is string pathBase ? Path.GetFullPath(pathBase, request.baseDirectory) : request.baseDirectory, settings.valuePathBases);
                definition = requested.Freeze();
            }
            await composition.ConfigureAsync(definition, extensionInputs, cancellation.Token).ConfigureAwait(false);
            FrozenExtensionRegistry registry = composition.registry;
            ManagedProjectionInputCapture? managedProjection = bridgeOnly ? null
                : ManagedProjectionInputCapture.Capture(configuration.api.projectionInputs, request.baseDirectory, paths, cancellation.Token,
                    includeFramework: !configuration.api.externalTypes.IsEmpty);
            List<string> protectedInputs = configuration.source.headers.Concat(configuration.source.includeDirectories).Concat(configuration.source.systemIncludeDirectories)
                .Select(path => Path.GetFullPath(path, request.baseDirectory)).ToList();
            protectedInputs.AddRange(configuration.bridge.nativeShims.SelectMany(shim => shim.publicHeaders.Concat(shim.sourceFiles)).Select(path => Path.GetFullPath(path, request.baseDirectory)));
            protectedInputs.AddRange(configuration.api.projectionInputs.sources.Concat(configuration.api.projectionInputs.references).Select(path => Path.GetFullPath(path, request.baseDirectory)));
            if (managedProjection is not null)
                protectedInputs.AddRange(managedProjection.snapshot.files.SelectMany(input => new[] { input.path, input.resolvedPath })
                    .Concat(managedProjection.snapshot.absentInputs.SelectMany(input => new[] { input.path, input.resolvedPath })));
            foreach (PluginRegistration plugin in definition.Plugins)
                if (plugin.Manifest is not null)
                {
                    new BGCS.Infrastructure.Plugins.PluginManifestReader(paths).Verify(plugin.Manifest);
                    protectedInputs.AddRange(plugin.Manifest.inputs.SelectMany(input => new[] { input.path, input.resolvedPath }));
                }
            if (request.configurationPath is not null) protectedInputs.Add(request.configurationPath);
            foreach (InputDependency input in request.configurationSnapshot.files)
            {
                protectedInputs.Add(input.path);
                protectedInputs.Add(input.resolvedPath);
                if (BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(input.path, paths) != input.resolvedPath)
                    throw new IOException("A composed configuration input changed its physical identity after request creation.");
                using FileStream stream = File.OpenRead(input.path);
                ContentDigest current = new(Convert.ToHexStringLower(SHA256.HashData(stream)));
                if (current != input.digest) throw new IOException("A composed configuration input changed after request creation.");
            }
            foreach (TargetConfiguration target in configuration.targets)
            {
                protectedInputs.AddRange(target.settings.systemIncludeDirectories.Concat(target.settings.cxxSystemIncludeDirectories).Select(path => Path.GetFullPath(path, request.baseDirectory)));
                if (target.settings.sysRoot is not null) protectedInputs.Add(Path.GetFullPath(target.settings.sysRoot, request.baseDirectory));
            }
            protectedInputs.AddRange(extensionInputs.snapshots.SelectMany(snapshot => snapshot.files.Select(input => input.path)
                .Concat(snapshot.searchDirectories.Select(input => input.path)).Concat(snapshot.absentInputs.Select(input => input.path))));
            string cacheRoot = Path.GetFullPath(configuration.cache.directory, request.baseDirectory);
            GenerationStageCache? reusableEmissionCache = null;
            string? unavailableCacheReason = null;
            if (configuration.cache.enabled)
            {
                string cachePhysical = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(cacheRoot, paths);
                foreach (string input in protectedInputs)
                {
                    string sourcePhysical = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(input, paths);
                    if (paths.IsWithin(cachePhysical, sourcePhysical) || paths.IsWithin(sourcePhysical, cachePhysical))
                        throw new IOException("The generation cache overlaps a declared source, SDK, configuration or plugin input.");
                }
                try
                {
                    reusableEmissionCache = new(cacheRoot, paths, configuration.cache.maximumBytes, configuration.cache.maximumEntryBytes);
                    reusableEmissionCache.Prepare();
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    reusableEmissionCache = null;
                    unavailableCacheReason = failure.Message;
                    diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "Emission cache preparation was unavailable: " + failure.Message, "BGCS-CACHE-UNAVAILABLE"));
                }
            }
            protectedInputs.Add(cacheRoot);
            using ArtifactPublisher publisher = new(output, configuration.library.id, paths, protectedInputs, cancellation.Token);
            string normalizedPreviewOutput = output.Replace('\\', '/').TrimEnd('/');
            string[] normalizedCandidateOrigins = [publisher.candidateRoot.Replace('\\', '/').TrimEnd('/'),
                paths.GetRelativePath(request.baseDirectory, publisher.candidateRoot).Replace('\\', '/').TrimEnd('/')];
            CandidateMetadataNormalizer metadataNormalizer = new(normalizedCandidateOrigins, logicalOutput,
                request.previewOutputRoot is null ? null : normalizedPreviewOutput, !paths.GetSemantics(logicalOutput).caseSensitive);
            FrontendGenerationCache primaryFrontendCache = new(reusableEmissionCache, unavailableCacheReason, extensionInputs, cacheEvidence, pendingCacheEntries, paths);
            DirectoryArtifactSink rootSink = new(publisher.candidateRoot, paths);
            HashSet<string> concreteTargets = new(StringComparer.Ordinal);
            List<SourceSnapshot> parsedInputs = [];
            foreach (TargetConfiguration target in configuration.targets)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                SourceConfiguration source = configuration.source;
                NativeSourceTextValidator.Validate(source.preamble, "source.preamble");
                NativeSourceTextValidator.Validate(source.postamble, "source.postamble");
                BGCS.NativeBridge.Shims.NativeShimPlanner shimPlanner = new();
                ImmutableArray<NativeShimPlan> shims = configuration.bridge.nativeShims.Select(shim => shimPlanner.Plan(shim, request.baseDirectory)).ToImmutableArray();
                ImmutableArray<string> parseHeaders = source.headers.Select(path => Path.GetFullPath(path, request.baseDirectory))
                    .Concat(shims.SelectMany(shim => shim.publicHeaders)).Distinct(paths.comparer).ToImmutableArray();
                ImmutableArray<string> userIncludes = NativeHeaderPlan.PrepareUserIncludes(source.includeDirectories, parseHeaders, request.baseDirectory, paths);
                SourceSnapshot declaredNativeInputs = new ExternalInputSnapshotBuilder().Capture(
                    parseHeaders.Concat(shims.SelectMany(shim => shim.sourceFiles)).Distinct(StringComparer.Ordinal)
                        .Select(path => new DeclaredExternalInput(path, ExternalInputKind.File)).ToImmutableArray(),
                    request.baseDirectory, paths, cancellation.Token);
                parsedInputs.Add(declaredNativeInputs);
                foreach (InputDependency shimInput in declaredNativeInputs.files.Where(input => shims.Any(shim => shim.sourceFiles.Contains(input.path))))
                    NativeSourceTextValidator.Validate(File.ReadAllText(shimInput.path), shimInput.path);
                TemplateInstantiationPlanner templatePlanner = new();
                TemplateInstantiationPlan? templates = source.language == SourceLanguage.Cpp
                    && (!configuration.bridge.cpp.templateInstantiations.types.IsEmpty || !configuration.bridge.cpp.templateInstantiations.functions.IsEmpty)
                    ? templatePlanner.Plan(configuration.bridge.cpp.templateInstantiations) : null;
                string supportPreamble = (templates?.preamble ?? string.Empty)
                    + (source.language == SourceLanguage.Cpp && !configuration.bridge.cpp.virtualCallbacks.IsEmpty ? VirtualCallbackPlanner.preamble : string.Empty);
                if (supportPreamble.Length != 0)
                    source = source with { preamble = source.preamble + "\n" + supportPreamble };
                NativeVariadicAdapterPlanner variadicPlanner = new();
                string nativePostamble = source.postamble + (templates is null ? string.Empty : "\n" + templates.postamble);
                if (source.language == SourceLanguage.Cpp && !configuration.bridge.cpp.sequenceInputAdapters.IsEmpty)
                    nativePostamble += StandardPathInputProbe.postamble;
                string variadicPostamble = variadicPlanner.TypeProbePostamble(source.variadicVariants, source.language);
                if (variadicPostamble.Length != 0) nativePostamble += "\n" + variadicPostamble;
                AnalysisRequest analysisRequest = new(parseHeaders, source.language, request.baseDirectory)
                {
                    targetId = target.id,
                    triple = target.triple,
                    languageStandard = source.languageStandard!,
                    includeDirectories = userIncludes,
                    systemIncludeDirectories = source.systemIncludeDirectories,
                    defines = source.defines,
                    arguments = source.arguments,
                    preamble = source.preamble,
                    postamble = nativePostamble,
                    parseMacros = source.parseMacros,
                    parseComments = source.parseComments,
                    parseSystemIncludes = source.includeSystemDeclarations,
                    snapshotExcludedDirectories = new[] { publisher.candidateRoot, output, logicalOutput, cacheRoot }.Distinct(paths.comparer).ToImmutableArray(),
                    toolchain = new NativeToolchainDescriptor(target.settings.compilerPath, target.settings.sysRoot,
                        target.settings.systemIncludeDirectories, target.settings.defines, target.settings.arguments, target.settings.cxxSystemIncludeDirectories)
                };
                SourceContributionCapture? contribution = null;
                AnalysisResult analysis = await AnalyzeCoreAsync(analysisRequest, applyTransforms: true, cancellation.Token, composition, extensionInputs, primaryFrontendCache,
                    async (
                        prepared,
                        token
                    ) => {
                        if (concreteTargets.Contains(prepared.target.targetId.value)) throw new InvalidOperationException("Two target selections resolve to the same concrete target.");
                        contribution = await SourceContributionCapture.CaptureAsync(registry, configuration, prepared, extensionInputs, !bridgeOnly, paths, token).ConfigureAwait(false);
                        if (contribution is not null) sourceContributions.Add(contribution);
                        return contribution?.Prepare(prepared) ?? prepared;
                    }).ConfigureAwait(false);
                diagnostics.AddRange(analysis.diagnostics);
                if (!analysis.success) break;
                FrontendRequest preparedSource = analysis.preparedFrontend ?? throw new InvalidOperationException("The original analysis omitted its prepared source inputs.");
                parseHeaders = preparedSource.headers;
                analysisRequest = analysisRequest with
                {
                    headers = preparedSource.headers,
                    includeDirectories = preparedSource.includeDirectories,
                    sourcePathMappings = preparedSource.sourcePathMappings
                };
                RejectFormalOutputReads(analysis.snapshot!);
                parsedInputs.Add(analysis.snapshot!);
                string targetId = analysis.module!.targetId;
                NativeCallPlanningStage? callPlanning = bridgeOnly ? null : new(registry, analysis.target!, configuration.api.runtime, extensionInputs, cancellation.Token);
                ManagedRuntimeProfile? runtimeProfile = callPlanning?.runtime;
                string emittedLibraryName = configuration.library.nativeName;
                if (!concreteTargets.Add(targetId)) throw new InvalidOperationException($"Two target selections resolve to '{targetId}'.");
                ImmutableArray<NativeGeneratedInputMap> contributionMaps = contribution?.Mappings(targetId) ?? [];
                if (contribution is not null) artifacts.AddRange(contribution.PublishNative(rootSink, targetId));
                NativeHeaderPlan nativeHeaderPlan = NativeHeaderPlan.Create(preparedSource, contributionMaps, publisher.candidateRoot, analysis.frontendExecutionInputs, paths);
                NativeModule originalModule = analysis.module!;
                HashSet<StableId> ordinaryDeclarations = originalModule.declarations.Select(declaration => declaration.id).ToHashSet();
                StableId? pathInputProbe = source.language == SourceLanguage.Cpp && !configuration.bridge.cpp.sequenceInputAdapters.IsEmpty
                    ? StandardPathInputProbe.Find(originalModule) : null;
                if (pathInputProbe is StableId privateUnit) ordinaryDeclarations.Remove(privateUnit);
                HashSet<string> requestedPublicDeclarations = new(StringComparer.Ordinal);
                if (templates is not null)
                {
                    HashSet<StableId> concreteCallables = templatePlanner.SelectCallables(originalModule, templates).ToHashSet();
                    ordinaryDeclarations.ExceptWith(originalModule.declarations.Where(declaration => declaration.kind == "Function" && !concreteCallables.Contains(declaration.id)).Select(declaration => declaration.id));
                    foreach (NativeDeclaration wrapper in originalModule.declarations.Where(declaration => declaration.kind == "Function"
                        && templates.functionWrappers.Values.Contains(declaration.qualifiedName, StringComparer.Ordinal)))
                        requestedPublicDeclarations.Add(wrapper.id.value);
                    HashSet<string> concreteTypes = templates.concreteTypes.Select(type => string.Concat(type.Where(character => !char.IsWhiteSpace(character)))).ToHashSet(StringComparer.Ordinal);
                    foreach (NativeType type in originalModule.types.Where(type => type.category == NativeTypeCategory.Record && type.declaration is not null
                        && concreteTypes.Contains(string.Concat(type.spelling.Where(character => !char.IsWhiteSpace(character))))))
                        requestedPublicDeclarations.Add(type.declaration!.Value.value);
                }
                foreach (NativeShimPlan shim in shims)
                    foreach (StableId callable in shimPlanner.SelectCallables(originalModule, shim)) requestedPublicDeclarations.Add(callable.value);
                ImmutableArray<CppVirtualProxyPlan> virtualProxies = configuration.bridge.cpp.virtualCallbacks.Select(callback =>
                    new VirtualCallbackPlanner().Plan(originalModule, callback, analysis.target!.sourceConventions
                        ?? throw new InvalidOperationException("Virtual callbacks require explicit target source conventions."))).ToImmutableArray();
                NativeVariadicAdapterPlan variadicAdapters = variadicPlanner.Plan(originalModule, source.variadicVariants);
                foreach (NativeVariadicAdapter adapter in variadicAdapters.adapters)
                    requestedPublicDeclarations.Add(adapter.sourceDeclaration.value);
                nativePostamble += "\n" + variadicAdapters.source.text;
                foreach (StableId probe in variadicAdapters.adapters.SelectMany(adapter => adapter.argumentTypes))
                    if (originalModule.GetType(probe).declaration is StableId probeDeclaration) ordinaryDeclarations.Remove(probeDeclaration);
                HashSet<string> requiredDeclarations = configuration.bridge.typeLowerings.Where(recipe => recipe.ownedResultRelease is not null)
                    .Select(recipe => recipe.ownedResultRelease!.nativeFunction).ToHashSet(StringComparer.Ordinal);
                string[] configuredReleaseSelectors = configuration.semantics.ownedResults.Where(rule => rule.releaseFunction is not null)
                    .Select(rule => rule.releaseFunction!).Distinct(StringComparer.Ordinal).ToArray();
                if (configuredReleaseSelectors.Length != 0)
                {
                    HashSet<string> sourceIdentities = analysis.module!.declarations.SelectMany(declaration => new[] { declaration.id.value, declaration.qualifiedName.TrimStart(':') }).ToHashSet(StringComparer.Ordinal);
                    foreach (NativeDeclaration callable in analysis.module.declarations.Where(declaration => declaration.kind == "Function"))
                    {
                        try { sourceIdentities.Add(new BGCS.Analysis.Types.NativeTypeFormatter().CallableSignature(analysis.module, callable)); }
                        catch (NotSupportedException) { } // Dependent template signatures are not native cleanup exports.
                    }
                    foreach (string selector in configuredReleaseSelectors)
                        if (sourceIdentities.Contains(selector.TrimStart(':'))) requiredDeclarations.Add(selector);
                }
                DeclarationSelection selection = new DeclarationSelector(paths.comparer).Select(analysis.module!, source.selection, source.headers.Concat(contribution?.exposedHeaders ?? []).ToImmutableArray(), request.baseDirectory,
                    source.includeTransitivelyReferencedHeaders, source.includeSystemDeclarations, requiredDeclarations, cancellation.Token,
                    additionalPublicDeclarations: requestedPublicDeclarations, ordinaryDeclarations: ordinaryDeclarations,
                    requiredMetadataDeclarations: virtualProxies.SelectMany(proxy => proxy.source.declarationDependencies)
                        .Concat(variadicAdapters.source.declarationDependencies).Select(id => id.value), includeCHeaderHelpers: source.includeHeaderHelpers,
                    requiredNativeTypes: variadicAdapters.adapters.SelectMany(adapter => adapter.argumentTypes)
                        .Concat(pathInputProbe is StableId pathUnit ? [StandardLibraryFactReader.Resolve(analysis.module!, analysis.module!.GetDeclaration(pathUnit).type!.Value).id] : []));
                NativeModule selectedModule = selection.module;
                ImmutableArray<StableId> selectedCallables = selection.callables;
                if (!variadicAdapters.adapters.IsEmpty) WriteJson(targetId + "/native/metadata/variadic.adapters.json", variadicAdapters);
                InteropModule interop;
                NativeBridgePlan? bridgeModel = null;
                SourceSnapshot? bridgeSnapshot = null;
                object? bridgeInputEvidence = null;
                InteropModule? originalC = source.language == SourceLanguage.C
                    ? ConfigurationSemantics.Apply(new CDeclarationAnalyzer().Analyze(selectedModule, configuration.library.id, selectedCallables,
                        CDeclarationAnalysisPurpose.NativeBridgePlanning), configuration, request.configurationPath ?? "sdk-configuration") : null;
                bool needsBridge = source.language == SourceLanguage.Cpp || bridgeOnly || !variadicAdapters.adapters.IsEmpty
                    || selectedCallables.Any(id => CHeaderFunctionPolicy.RequiresAdapter(selectedModule.GetDeclaration(id)))
                    || !registry.NativeTypes.IsEmpty || !registry.NativeCallables.IsEmpty
                    || !configuration.bridge.typeLowerings.IsEmpty || !configuration.bridge.callableLowerings.IsEmpty
                    || originalC is not null && configuration.bridge.aggregateStrategy == AggregateCallStrategy.PointerBridge
                        && NativeCallPlanningStage.HasAggregateBoundary(originalC);
                if (needsBridge)
                {
                    string prefix = configuration.library.bridgeExportPrefix ?? "bgcs_" + Regex.Replace(configuration.library.id, "[^A-Za-z0-9]+", "_", RegexOptions.CultureInvariant).Trim('_');
                    List<object> loweringServices = [];
                    NativeBridgePlan bridge;
                    try
                    {
                        List<(ExtensionDescriptor descriptor, INativeTypeLowering service)> typeLowerings = [];
                        foreach (TypeLoweringRecipe recipe in configuration.bridge.typeLowerings)
                            typeLowerings.Add((new("configured-type:" + recipe.id, ExtensionPort.NativeTypeLowering), new BGCS.NativeBridge.Recipes.LoweringRecipeCompiler().Compile(recipe with
                            { sourceFragments = recipe.sourceFragments.Select(fragment => fragment with { requiredHeaders = fragment.requiredHeaders.Select(path => Path.GetFullPath(path, request.baseDirectory)).ToImmutableArray() }).ToImmutableArray() })));
                        List<(ExtensionDescriptor descriptor, INativeCallableLowering service)> callableLowerings = [];
                        foreach (ServiceFactory<INativeTypeLowering> registration in registry.NativeTypes)
                        {
                            INativeTypeLowering service = registration.Factory() ?? throw new InvalidOperationException("A C++ type lowering factory returned no service.");
                            loweringServices.Add(service);
                            extensionInputs.Capture(service, analysis.target!, cancellation.Token);
                            typeLowerings.Add((registration.Descriptor, service));
                        }
                        foreach (ServiceFactory<INativeCallableLowering> registration in registry.NativeCallables)
                        {
                            INativeCallableLowering service = registration.Factory() ?? throw new InvalidOperationException("A C++ callable lowering factory returned no service.");
                            loweringServices.Add(service);
                            extensionInputs.Capture(service, analysis.target!, cancellation.Token);
                            callableLowerings.Add((registration.Descriptor, service));
                        }
                        bridge = new NativeBridgePlanner(new LoweringRegistry(typeLowerings, callableLowerings), configuration.bridge.callableLowerings.Select(recipe => recipe with
                        { sourceFragments = recipe.sourceFragments.Select(fragment => fragment with { requiredHeaders = fragment.requiredHeaders.Select(path => Path.GetFullPath(path, request.baseDirectory)).ToImmutableArray() }).ToImmutableArray() }), configuration.bridge.cpp.familyBindings).Plan(new(selectedModule, analysis.target!, prefix,
                            parseHeaders)
                        { semantics = originalC }, selectedCallables, cancellation.Token, publicDeclarations: selection.publicDeclarations, virtualProxies: virtualProxies, variadicAdapters: variadicAdapters.adapters);
                        bridge = bridge with
                        {
                            preamble = source.preamble,
                            postamble = nativePostamble,
                            headers = parseHeaders.Select(path => nativeHeaderPlan.Include(path, targetId + (source.language == SourceLanguage.C ? "/native/include" : "/native/src"))).ToImmutableArray(),
                            sourceFragments = bridge.sourceFragments.Select(fragment => fragment with
                            { requiredHeaders = fragment.requiredHeaders.Select(path => nativeHeaderPlan.Include(path, targetId + (source.language == SourceLanguage.C ? "/native/include" : "/native/src"))).ToImmutableArray() }).ToImmutableArray()
                        };
                    }
                    finally
                    {
                        for (int index = loweringServices.Count - 1; index >= 0; index--) await Release(loweringServices[index]).ConfigureAwait(false);
                    }
                    if (HasErrors()) break;
                    if (source.language == SourceLanguage.Cpp)
                    {
                        bridge = new SequenceInputAdapterPlanner().Apply(bridge, configuration.bridge.cpp.sequenceInputAdapters, cancellation.Token);
                        new CppBridgeLanguageValidator().Validate(bridge, source.languageStandard!);
                    }
                    bridgeModel = bridge;
                    string nativePrefix = targetId + "/native/";
                    ImmutableArray<ArtifactDescriptor> native = new TypedBridgeEmitter().Emit(bridge, new PrefixSink(rootSink, nativePrefix), prefix, cancellation.Token);
                    artifacts.AddRange(native.Select(artifact => artifact with { relativePath = nativePrefix + artifact.relativePath }));
                    AnalysisResult reparsed = await AnalyzeCoreAsync(analysisRequest with
                    {
                        headers = [Path.Combine(publisher.candidateRoot, targetId, "native", "include", prefix + ".h")],
                        sourcePathMappings = analysisRequest.sourcePathMappings.SetItem(Path.Combine(publisher.candidateRoot, targetId, "native", "include", prefix + ".h"),
                            targetId + "/native/include/" + prefix + ".h"),
                        language = SourceLanguage.C,
                        languageStandard = "c17",
                        preamble = string.Empty,
                        postamble = string.Empty,
                        parseMacros = false,
                        parseComments = false,
                        parseSystemIncludes = false
                    }, applyTransforms: false, cancellation.Token, composition, extensionInputs).ConfigureAwait(false);
                    diagnostics.AddRange(reparsed.diagnostics);
                    if (!reparsed.success) break;
                    interop = new BridgeContractVerifier().VerifyAndMerge(bridge, reparsed.module!, configuration.library.id);
                    bridgeSnapshot = reparsed.snapshot;
                    bridgeInputEvidence = NativeSourceInputIdentity.Evidence(bridgeSnapshot!, reparsed.preparedFrontend!.sourcePathMappings, paths);
                    parsedInputs.Add(bridgeSnapshot!);
                    WriteJson(nativePrefix + "metadata/bridge.module.json", bridge);
                }
                else interop = originalC ?? throw new InvalidOperationException("A C generation request lost its native semantic module.");
                interop = new NativeConstantAnalyzer().Apply(selectedModule, interop, selection.publicDeclarations);
                diagnostics.AddRange(interop.diagnostics.Where(diagnostic => !analysis.diagnostics.Contains(diagnostic)));
                if (HasErrors()) break;
                // C contracts were established on original declarations and are
                // preserved by bridge verification. Rebinding would overwrite
                // that evidence and could confuse logical and physical channels.
                if (originalC is null)
                    interop = ConfigurationSemantics.Apply(interop, configuration, request.configurationPath ?? "sdk-configuration");
                diagnostics.AddRange(interop.diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));
                if (HasErrors()) break;
                IManagedProjection? projection = null;
                IBindingBackend? backend = null;
                List<IInteropTransform> transforms = [];
                List<IManagedApiTransform> managedTransforms = [];
                List<(ServiceFactory<IArtifactProcessor> Registration, IArtifactProcessor Service)> preparedProcessors = [];
                ImmutableArray<ArtifactDescriptor> emitted = [];
                ManagedApiModel? managedModel = null;
                BGCS.CSharp.Planning.CSharpEmissionPlan? emittedPlan = null;
                StaticExportTableDescriptor? staticTable = null;
                try
                {
                    InteropModule baseline = interop;
                    AnalysisContext transformContext = new(analysis.target!, configuration.source.language, analysis.snapshot!);
                    foreach (ServiceFactory<IInteropTransform> registration in registry.InteropTransforms)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        IInteropTransform transform = registration.Factory() ?? throw new InvalidOperationException($"Interoperability transformation '{registration.Id}' has no service.");
                        transforms.Add(transform);
                        extensionInputs.Capture(transform, analysis.target!, cancellation.Token);
                        interop = transform.Transform(interop, transformContext, cancellation.Token) ?? throw new InvalidOperationException("An interoperability transformation returned no model.");
                        new InteropModuleVerifier().VerifyPreservedAbi(baseline, interop);
                        diagnostics.AddRange(interop.diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));
                        if (HasErrors()) break;
                    }
                    if (HasErrors()) break;
                    new BGCS.Analysis.Bridge.NativeValueProtocolVerifier().Verify(interop);
                    diagnostics.AddRange(new BGCS.Analysis.Safety.SafetyAnalyzer().Analyze(interop).Where(diagnostic => !diagnostics.Contains(diagnostic)));
                    if (HasErrors()) break;
                    if (runtimeProfile?.moduleMechanism == NativeModuleMechanism.StaticAdapter && configuration.api.importMode == BindingImportMode.FunctionTable)
                    {
                        var adapter = StaticExportTableEmitter.Emit(interop,
                            bridgeModel is null ? parseHeaders.Select(path => nativeHeaderPlan.Include(path, targetId + (source.language == SourceLanguage.C ? "/native/include" : "/native/src"))).ToImmutableArray()
                                : [nativeHeaderPlan.IncludeArtifact(targetId + "/native/include/" + bridgeModel.exportPrefix + ".h", targetId + "/native/src")]);
                        staticTable = adapter.descriptor;
                        string adapterPath = targetId + "/native/src/export.table.c";
                        string adapterSource = (bridgeModel is null ? source.preamble + "\n" : string.Empty) + adapter.source;
                        rootSink.WriteText(adapterPath, adapterSource);
                        artifacts.Add(new(adapterPath, "text/x-c", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(adapterSource))))));
                    }
                    if (!bridgeOnly)
                    {
                        projection = registry.Projection() ?? throw new InvalidOperationException("The managed projection factory returned no service.");
                        extensionInputs.Capture(projection, analysis.target!, cancellation.Token);
                        backend = registry.Backend() ?? throw new InvalidOperationException("The backend factory returned no service.");
                        extensionInputs.Capture(backend, analysis.target!, cancellation.Token);
                        ManagedApiModel model = projection.Project(interop, new(configuration.api.@namespace, configuration.api.name)
                        {
                            style = configuration.api.style,
                            names = configuration.api.names,
                            enums = configuration.api.enums,
                            enumMappings = configuration.api.enumMappings,
                            functionAliases = configuration.api.functionAliases,
                            naming = configuration.api.naming,
                            functionContainers = configuration.api.functionContainers,
                            functions = configuration.api.functions,
                            records = configuration.api.records,
                            memberFunctions = configuration.api.memberFunctions,
                            pointerCarriers = configuration.api.pointerCarriers,
                            pointerFunctions = configuration.api.pointerFunctions,
                            emitLibraryNameConstant = configuration.api.emitLibraryNameConstant,
                            importLibraryNameConstant = configuration.api.importLibraryNameConstant,
                            emitNativeMetadata = configuration.api.emitNativeMetadata,
                            nestGeneratedTypesInApi = configuration.api.nestGeneratedTypesInApi,
                            emitPlaceholderComments = configuration.api.emitPlaceholderComments,
                            emitCallbackDelegates = configuration.api.emitCallbackDelegates,
                            constants = configuration.api.constants,
                            nativeEnums = configuration.api.nativeEnums,
                            defaultOverloadLimit = configuration.api.defaultOverloadLimit
                        }, cancellation.Token);
                        if (!ReferenceEquals(model.interop, interop)) throw new InvalidOperationException("A managed projection changed the frozen native ABI instead of its presentation.");
                        if (bridgeModel is not null) model = new BGCS.CSharp.Projection.ManagedRecipeProjectionBinder().Apply(model, bridgeModel, configuration.bridge.typeLowerings);
                        foreach (ServiceFactory<IManagedApiTransform> registration in registry.ManagedTransforms)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            IManagedApiTransform transform = registration.Factory()
                                ?? throw new InvalidOperationException($"Managed transformation '{registration.Id}' has no service.");
                            managedTransforms.Add(transform);
                            extensionInputs.Capture(transform, analysis.target!, cancellation.Token);
                            model = transform.Transform(model, transformContext, cancellation.Token)
                                ?? throw new InvalidOperationException("A managed transformation returned no model.");
                            if (!ReferenceEquals(model.interop, interop))
                                throw new InvalidOperationException("A managed transformation changed the frozen native ABI instead of its presentation.");
                        }
                        model = ExternalProjectionConfigurationBinder.Bind(model, configuration.api.externalTypes);
                        if (backend is BGCS.CSharp.CSharpBackend)
                            model = BGCS.CSharp.Projection.StaticExternalProjectionBinder.Bind(model, managedProjection?.inputs, cancellation.Token);
                        managedModel = model;
                        string runtimeNamespace = configuration.runtime.mode == "source"
                            ? configuration.runtime.@namespace ?? configuration.api.@namespace + ".Runtime" : "BGCS.Runtime";
                        if (configuration.runtime.mode == "package" && configuration.runtime.@namespace is not null && configuration.runtime.@namespace != "BGCS.Runtime")
                            throw new ArgumentException("Package runtime references require the BGCS.Runtime namespace.");
                        managedProjection?.Verify(cancellation.Token);
                        var finalCalls = callPlanning!.PlanFinal(interop, cancellation.Token);
                        diagnostics.AddRange(finalCalls.diagnostics);
                        if (HasErrors()) break;
                        EmissionRequest emission = new(emittedLibraryName, new PrefixSink(rootSink, targetId + "/managed/"))
                        {
                            framework = configuration.api.framework,
                            maximumValueStringCodeUnits = configuration.api.maximumValueStringCodeUnits,
                            importMode = configuration.api.importMode,
                            additionalUsings = configuration.api.usings,
                            managedRuntime = runtimeProfile,
                            callPlans = finalCalls.plans,
                            staticExportTable = staticTable,
                            runtimeNamespace = runtimeNamespace,
                            includeRuntimeSource = configuration.runtime.mode == "source",
                            additionalRuntimeSourceFeatures = configuration.runtime.sourceFeatures,
                            singleFile = configuration.output.layout == "singleFile",
                            singleFileName = configuration.output.singleFileName,
                            managedProjection = managedProjection?.inputs
                        };
                        if (backend is BGCS.CSharp.CSharpBackend)
                        {
                            emittedPlan = new BGCS.CSharp.Planning.CSharpEmissionPlanner().Plan(model, emission, cancellation.Token);
                            foreach (var omission in emittedPlan.virtualProxyOmissions)
                                diagnostics.Add(new(BindingDiagnosticSeverity.Warning,
                                    "Managed virtual callback registration '" + omission.proxy.value + "' was omitted: " + omission.reason,
                                    "BGCS-FRIENDLY-OMITTED")
                                { location = omission.source });
                            foreach (var omission in emittedPlan.callbackOmissions)
                                diagnostics.Add(new(BindingDiagnosticSeverity.Warning,
                                    "Managed callback parameter '" + omission.callbackParameter.value + "' was omitted while preserving its raw ABI: " + omission.reason,
                                    "BGCS-FRIENDLY-OMITTED")
                                { location = omission.source });
                            foreach (var omission in emittedPlan.valueParameterOmissions)
                                diagnostics.Add(new(BindingDiagnosticSeverity.Warning,
                                    "Managed single-value pointer '" + omission.parameter.value + "' was omitted while preserving its raw ABI: " + omission.reason,
                                    "BGCS-FRIENDLY-OMITTED")
                                { location = omission.source });
                            foreach (var omission in emittedPlan.ownedProjectionOmissions)
                                diagnostics.Add(new(BindingDiagnosticSeverity.Warning,
                                    "Managed C ownership projection '" + omission.nativeExport.value + "' was omitted while preserving its raw ABI: " + omission.reason,
                                    "BGCS-FRIENDLY-OMITTED")
                                { location = omission.source });
                        }
                        if (HasErrors()) break;
                        foreach (ServiceFactory<IArtifactProcessor> registration in ConfiguredSourceProcessors.Prepare(configuration.output.processors, registry.Processors))
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            IArtifactProcessor processor = registration.Factory() ?? throw new InvalidOperationException("A source processor factory returned no service.");
                            pendingProcessorRelease.Add(processor);
                            preparedProcessors.Add((registration, processor));
                            extensionInputs.Capture(processor, analysis.target!, cancellation.Token);
                        }
                        managedProjection?.Verify(cancellation.Token);
                        GenerationStageCache? stageCache = null;
                        ContentDigest stageKey = default;
                        string? cacheReason = !configuration.cache.enabled ? "Caching is disabled by configuration."
                            : reusableEmissionCache is null ? unavailableCacheReason ?? "The cache directory could not be prepared."
                            : backend is not BGCS.CSharp.CSharpBackend ? "The selected backend has no verified reusable emission stage."
                            : extensionInputs.cacheIneligibilityReasons.Count != 0 ? "Input effects do not have a complete reusable cache identity: " + string.Join(", ", extensionInputs.cacheIneligibilityReasons.Order(StringComparer.Ordinal))
                            : extensionInputs.unfingerprintedServices.Count != 0 ? "An upstream extension did not declare a complete fingerprint: " + string.Join(", ", extensionInputs.unfingerprintedServices.Order(StringComparer.Ordinal))
                            : null;
                        if (cacheReason is null)
                        {
                            try
                            {
                                stageCache = reusableEmissionCache;
                                stageKey = EmissionStageCache.CreateKey(model, emission, configuration, analysis.snapshot!, declaredNativeInputs, extensionInputs, definition, cancellation.Token);
                            }
                            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
                            {
                                stageCache = null;
                                cacheReason = failure.Message;
                                diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "Emission caching was unavailable: " + failure.Message, "BGCS-CACHE-UNAVAILABLE"));
                            }
                        }
                        ImmutableArray<CachedStageArtifact> cached = [];
                        bool cacheHit = false;
                        string? cacheMiss = null;
                        if (stageCache is not null)
                        {
                            try { cacheHit = stageCache.TryRead(stageKey, out cached, out cacheMiss, cancellation.Token); }
                            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                            {
                                stageCache = null;
                                cacheReason = failure.Message;
                                diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "Emission cache lookup was unavailable: " + failure.Message, "BGCS-CACHE-UNAVAILABLE"));
                            }
                        }
                        if (cacheHit)
                        {
                            try
                            {
                                new BGCS.CSharp.Validation.EmittedInteropContractReader().Verify(emittedPlan!, cached.Select(artifact =>
                                    (artifact.descriptor.relativePath, new UTF8Encoding(false, true).GetString(artifact.content.Span))).ToArray(), emittedLibraryName, cancellation.Token);
                            }
                            catch (Exception failure) when (failure is InvalidOperationException or DecoderFallbackException)
                            {
                                cacheHit = false;
                                cacheMiss = "The cached emission failed its current syntax or ABI contract validation: " + failure.Message;
                            }
                        }
                        if (cacheHit)
                        {
                            foreach (CachedStageArtifact artifact in cached)
                            {
                                cancellation.Token.ThrowIfCancellationRequested();
                                emission.sink.WriteBytes(artifact.descriptor.relativePath, artifact.content);
                            }
                            emitted = cached.Select(artifact => artifact.descriptor).ToImmutableArray();
                        }
                        else if (stageCache is not null)
                        {
                            EmissionStageCache.CapturingSink capture = new(emission.sink, configuration.cache.maximumEntryBytes);
                            emitted = await backend.EmitAsync(model, emission with { sink = capture }, cancellation.Token).ConfigureAwait(false);
                            ImmutableArray<CachedStageArtifact> captured = capture.Complete(emitted);
                            if (!captured.IsEmpty || emitted.IsEmpty) pendingCacheEntries.Add((stageCache, stageKey, captured));
                            else cacheMiss = "The complete emission exceeds the cache entry budget.";
                        }
                        else emitted = await backend.EmitAsync(model, emission, cancellation.Token).ConfigureAwait(false);
                        cacheEvidence.Add(new(targetId, "csharpEmission", cacheHit ? "hit" : stageCache is not null ? "miss" : "disabled", cacheReason ?? cacheMiss));
                        if (cacheMiss is not null) diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "Emission cache entry was not reused: " + cacheMiss, "BGCS-CACHE-MISS"));
                    }
                }
                finally
                {
                    if (backend is not null) await Release(backend).ConfigureAwait(false);
                    for (int index = managedTransforms.Count - 1; index >= 0; index--) await Release(managedTransforms[index]).ConfigureAwait(false);
                    if (projection is not null) await Release(projection).ConfigureAwait(false);
                    for (int index = transforms.Count - 1; index >= 0; index--) await Release(transforms[index]).ConfigureAwait(false);
                }
                if (HasErrors()) break;
                if (!bridgeOnly && contribution is not null) emitted = emitted.AddRange(contribution.PublishManaged(rootSink, targetId));
                if (!bridgeOnly && (preparedProcessors.Count != 0 || contribution is not null && configuration.output.layout == "singleFile"))
                {
                    if (emittedPlan is null && registry.Validators.IsDefaultOrEmpty) throw new NotSupportedException("A custom backend with source processors requires its own final artifact validation gate.");
                    using CandidateTextEditor editor = new(publisher.candidateRoot, emitted.Select(artifact => artifact with { relativePath = targetId + "/managed/" + artifact.relativePath }), paths, targetId + "/managed/");
                    try
                    {
                        foreach ((ServiceFactory<IArtifactProcessor> registration, IArtifactProcessor processor) in preparedProcessors)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            await processor.ProcessAsync(new(managedModel!, editor.artifacts, editor), cancellation.Token).ConfigureAwait(false);
                        }
                        if (editor.artifacts.Any(artifact => artifact.relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && artifact.mediaType != "text/x-csharp"))
                            throw new InvalidOperationException("A processed C# candidate has an inconsistent managed media type.");
                        if (configuration.output.layout == "singleFile")
                        {
                            ArtifactDescriptor[] units = editor.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp").ToArray();
                            List<(string Path, string Text)> texts = [];
                            foreach (ArtifactDescriptor unit in units) texts.Add((unit.relativePath, await editor.ReadTextAsync(unit.relativePath, cancellation.Token).ConfigureAwait(false)));
                            string composed = new BGCS.CSharp.Emission.SingleFileComposer().ComposeSources(texts, managedModel!.@namespace, targetId);
                            string singlePath = targetId + "/managed/" + configuration.output.singleFileName;
                            if (units.Any(unit => unit.relativePath == singlePath)) await editor.ReplaceTextAsync(singlePath, composed, cancellation.Token).ConfigureAwait(false);
                            else await editor.AddTextAsync(singlePath, "text/x-csharp", composed, cancellation.Token).ConfigureAwait(false);
                            foreach (ArtifactDescriptor unit in units.Where(unit => unit.relativePath != singlePath)) await editor.DeleteTextAsync(unit.relativePath, cancellation.Token).ConfigureAwait(false);
                        }
                        emitted = editor.artifacts.Select(artifact => artifact with { relativePath = artifact.relativePath[(targetId.Length + "/managed/".Length)..] }).ToImmutableArray();
                        if (emittedPlan is not null)
                        {
                            List<(string Path, string Text)> actual = [];
                            foreach (ArtifactDescriptor artifact in editor.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"))
                                actual.Add((artifact.relativePath, await editor.ReadTextAsync(artifact.relativePath, cancellation.Token).ConfigureAwait(false)));
                            new BGCS.CSharp.Validation.EmittedInteropContractReader().Verify(emittedPlan, actual.ToArray(), emittedLibraryName, cancellation.Token);
                        }
                    }
                    finally
                    {
                        editor.Dispose();
                        for (int index = preparedProcessors.Count - 1; index >= 0; index--)
                        {
                            IArtifactProcessor processor = preparedProcessors[index].Service;
                            await Release(processor).ConfigureAwait(false);
                            int pending = pendingProcessorRelease.FindLastIndex(service => ReferenceEquals(service, processor));
                            if (pending >= 0) pendingProcessorRelease.RemoveAt(pending);
                        }
                    }
                    if (HasErrors()) break;
                }
                ImmutableArray<BindingCapabilityReport> targetCapabilities = CapabilityReportStage.Merge(originalModule, selection, interop,
                    bridgeModel, runtimeProfile, emittedPlan?.capabilities ?? [], bridgeOnly);
                capabilities.AddRange(targetCapabilities);
                WriteJson(targetId + "/metadata/capability.report.json", targetCapabilities);
                targetModels.Add(new(targetId, selectedModule, bridgeModel, interop, managedModel)
                {
                    resolvedTarget = analysis.target,
                });
                artifacts.AddRange(emitted.Select(artifact => artifact with { relativePath = targetId + "/managed/" + artifact.relativePath }));
                if (bridgeModel is not null || staticTable is not null)
                {
                    ImmutableArray<string> nativeHeaders = parseHeaders.Select(path => nativeHeaderPlan.Include(path, targetId + (source.language == SourceLanguage.C ? "/native/include" : "/native/src"))).ToImmutableArray();
                    string assertionPath = targetId + "/native/src/storage.assertions." + (source.language == SourceLanguage.C ? "c" : "cpp");
                    string assertions = new NativeStorageAssertionEmitter().Emit(selectedModule, nativeHeaders,
                        analysis.target!.sourceConventions ?? throw new InvalidOperationException("Native assertions require explicit target source conventions."),
                        source.preamble, nativePostamble);
                    rootSink.WriteText(assertionPath, assertions);
                    artifacts.Add(new(assertionPath, source.language == SourceLanguage.C ? "text/x-c" : "text/x-c++",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(assertions))))));
                }
                foreach (ArtifactDescriptor nativeArtifact in artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/", StringComparison.Ordinal)
                    && artifact.mediaType is "text/x-c" or "text/x-c++" or "text/x-c-header" or "text/x-c++-header"))
                    NativeSourceTextValidator.Validate(File.ReadAllText(Path.Combine(publisher.candidateRoot, nativeArtifact.relativePath)), nativeArtifact.relativePath);
                NativeSourceManifest nativeManifest = nativeHeaderPlan.Manifest(targetId, artifacts.ToImmutable(), analysis.snapshot!, declaredNativeInputs, shims,
                    new NativeBindingModule(configuration.library.id, emittedLibraryName),
                    interop.functions.Select(function => new NativeExportBinding(function.id, function.sourceDeclaration, function.exportName, configuration.library.id)).ToImmutableArray(),
                    bridgeOnly ? null : new NativeRuntimeRequirements(runtimeProfile!, configuration.api.importMode),
                    bridgeModel?.languageRequirements ?? [], staticTable, cancellation.Token);
                BGCS.Infrastructure.Verification.NativeSourceManifestVerifier.Validate(nativeManifest, artifacts.ToImmutable());
                nativeManifests.Add(nativeManifest);
                nativeInputRoots.AddRange(nativeHeaderPlan.Bindings(targetId, output));
                WriteJson(targetId + "/metadata/native-source.manifest.json", nativeManifest);
                WriteJson(targetId + "/metadata/abi.manifest.json", new
                {
                    schemaVersion = 1,
                    module = interop,
                    managedApiStyle = emittedPlan?.api.style,
                    nestedGeneratedTypesInApi = managedModel?.nestGeneratedTypesInApi ?? false,
                    managedInputVariations = emittedPlan?.inputVariations,
                    managedContextName = emittedPlan?.contextName,
                    sharedContext = emittedPlan?.sharedContext,
                    managedVirtualProxies = emittedPlan?.virtualProxies,
                    managedVirtualProxyOmissions = emittedPlan?.virtualProxyOmissions,
                    managedCallScopedCallbacks = emittedPlan?.callScopedCallbacks,
                    managedRetainedCallbacks = emittedPlan?.retainedCallbacks,
                    managedAsyncOperations = emittedPlan?.asyncOperations,
                    namedAbiDelegates = emittedPlan?.abiDelegates,
                    managedCallbackOmissions = emittedPlan?.callbackOmissions,
                    managedValueParameterOmissions = emittedPlan?.valueParameterOmissions,
                    managedCOwnedFactories = emittedPlan?.directOwnedFactories,
                    managedCModuleBorrowedFactories = emittedPlan?.directBorrowedFactories,
                    managedStringResults = emittedPlan?.stringResults,
                    managedValueStringResults = emittedPlan?.valueStringResults,
                    managedNullDefaultMethods = emittedPlan?.nullDefaultMethods,
                    managedCOwnershipOmissions = emittedPlan?.ownedProjectionOmissions,
                    managedCallAbi = emittedPlan?.managedRuntime?.callAbi,
                    managedAggregateCalls = emittedPlan?.aggregateCalls,
                    opaqueCallbackAddresses = emittedPlan?.opaqueCallables,
                    managedConstants = emittedPlan?.constants,
                    managedStringConstants = emittedPlan?.stringConstants,
                    managedNativeEnumPresentation = emittedPlan?.storage.Where(storage => storage.enumUnderlyingType is not null)
                        .Select(storage => new { storage.nativeType, storage.name, storage.enumUnderlyingType, storage.enumValues }),
                    managedLibrary = new
                    {
                        emitLibraryNameConstant = managedModel?.emitLibraryNameConstant ?? false,
                        nativeName = emittedLibraryName,
                        importLibraryNameConstant = emittedPlan?.importLibraryName
                    },
                    managedStorage = emittedPlan?.storage.Select(storage => new
                    { storage.nativeType, storage.size, storage.alignment, storage.packing, storage.managedAlignment })
                });
                List<IGenerationValidator> validators = [];
                try
                {
                    ImmutableArray<ArtifactDescriptor> candidates = artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/", StringComparison.Ordinal)).ToImmutableArray();
                    ValidationRequest validation = new(analysis.target!, interop, candidates, new DirectoryArtifactReader(publisher.candidateRoot, candidates, paths))
                    ;
                    foreach (ServiceFactory<IGenerationValidator> registration in registry.Validators)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        IGenerationValidator validator = registration.Factory() ?? throw new InvalidOperationException("A validation factory returned no gate.");
                        validators.Add(validator);
                        extensionInputs.Capture(validator, analysis.target!, cancellation.Token);
                        ImmutableArray<BindingDiagnostic> checks = await validator.ValidateAsync(validation, cancellation.Token).ConfigureAwait(false);
                        if (checks.IsDefault || checks.Any(diagnostic => diagnostic is null || !Enum.IsDefined(diagnostic.severity))) throw new InvalidOperationException("A validator returned an incomplete or invalid diagnostic collection.");
                        diagnostics.AddRange(checks);
                        if (HasErrors()) break;
                    }
                }
                finally
                {
                    for (int index = validators.Count - 1; index >= 0; index--) await Release(validators[index]).ConfigureAwait(false);
                }
                if (HasErrors()) break;
                WriteJson(targetId + "/metadata/verification.json", new
                {
                    schemaVersion = 1,
                    targetId,
                    framework = configuration.api.framework,
                    importMode = configuration.api.importMode.ToString(),
                    resolved = true,
                    parsed = true,
                    generated = true,
                    managedGenerated = !bridgeOnly,
                    managedRuntime = runtimeProfile,
                    staticExportTable = staticTable,
                    implementation = typeof(BindingHost).Assembly.ManifestModule.ModuleVersionId.ToString(),
                    cache = cacheEvidence.Where(evidence => evidence.targetId == targetId).ToArray(),
                    inputs = analysis.snapshot,
                    nativeInputRoots = nativeHeaderPlan.Bindings(targetId, logicalOutput),
                    bridgeInputs = bridgeInputEvidence,
                    configurationInputs = request.configurationSnapshot.files,
                    managedProjectionInputs = managedProjection?.snapshot,
                    managedProjectionEnvironment = managedProjection is null ? null : new
                    {
                        assemblies = managedProjection.inputs.assemblies.Select(input => new
                        { input.path, input.assemblyIdentity, input.digest, length = input.image.Length, input.isFramework }).ToArray(),
                        managedProjection.inputs.defines
                    },
                    extensionInputs = extensionInputs.snapshots,
                    plugins = definition.Plugins.Select(plugin => new { descriptor = plugin.Descriptor, settings = plugin.Configuration?.settings, pathBase = plugin.Configuration?.defaultPathBase, valuePathBases = plugin.Configuration?.valuePathBases, inputs = plugin.Manifest?.inputs, absentInputs = plugin.Manifest?.missingInputs }).ToArray(),
                    validationGates = registry.Validators.Select(validator => new { id = validator.Id, priority = validator.Priority }).ToArray()
                });

            }
            for (int index = pendingProcessorRelease.Count - 1; index >= 0; index--) await Release(pendingProcessorRelease[index]).ConfigureAwait(false);
            pendingProcessorRelease.Clear();
            await ReleaseGenerationServicesAsync().ConfigureAwait(false);
            if (!HasErrors())
            {
                managedProjection?.Verify(cancellation.Token);
                await extensionInputs.VerifyAsync(cancellation.Token).ConfigureAwait(false);
                bool parsedInputPhase = false;
                if (SourceSnapshotBatch.GetMismatch(FinalInputSnapshots(), paths, cancellation.Token) is string mismatch)
                    throw new IOException((parsedInputPhase
                        ? "Native file bytes or include-search membership changed during generation; the candidate cannot be published. "
                        : "A declared input changed while compiler observations were revalidated. ") + mismatch);

                IEnumerable<SourceSnapshot> FinalInputSnapshots()
                {
                    // The batch verifies each yielded snapshot before advancing
                    // this private Host iterator. Keep the sealed, read-only
                    // store/path checks between the original two sequences so
                    // duplicate input checks share one fence without changing
                    // first-failure precedence or invoking an extension here.
                    foreach (SourceSnapshot snapshot in extensionInputs.snapshots) yield return snapshot;
                    foreach (SourceContributionCapture capture in sourceContributions) capture.Verify();
                    foreach (SourceSnapshot external in extensionInputs.snapshots)
                    {
                        RejectFormalOutputReads(external);
                        string destination = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(logicalOutput, paths);
                        foreach (DirectoryInputDependency directory in external.searchDirectories)
                        {
                            if (paths.IsWithin(destination, directory.resolvedPath))
                                throw new IOException("A declared extension input directory overlaps the formal output it would replace.");
                        }
                    }
                    parsedInputPhase = true;
                    foreach (SourceSnapshot snapshot in parsedInputs) yield return snapshot;
                }
                foreach (InputDependency input in request.configurationSnapshot.files)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(input.path, paths) != input.resolvedPath) throw new IOException("A composed configuration input changed its physical identity before publication.");
                    using FileStream stream = File.OpenRead(input.path);
                    if (stream.Length != input.length || new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))) != input.digest)
                        throw new IOException("A composed configuration input changed during generation; the candidate cannot be published.");
                }
                foreach (PluginRegistration plugin in definition.Plugins)
                    if (plugin.Manifest is not null) new BGCS.Infrastructure.Plugins.PluginManifestReader(paths).Verify(plugin.Manifest);
                managedProjection?.Verify(cancellation.Token);
                WriteJson("generation.report.json", new
                {
                    schemaVersion = 1,
                    libraryId = configuration.library.id,
                    targets = concreteTargets.Order(StringComparer.Ordinal).ToArray(),
                    diagnostics = diagnostics.ToImmutable(),
                    cache = cacheEvidence,
                    bridgeOnly
                });
                paths.Verify();
                publisher.Commit(artifacts, cancellation.Token);
                published = true;
                if (publisher.cleanupFailure is not null)
                    diagnostics.Add(new(BindingDiagnosticSeverity.Warning, publisher.cleanupFailure.Message, "BGCS-PUBLICATION-CLEANUP"));
                foreach (var entry in pendingCacheEntries)
                    try
                    {
                        if (cancellation.IsCancellationRequested) break;
                        if (!entry.Cache.Store(entry.Key, entry.Artifacts, CancellationToken.None))
                            diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "A successful emission exceeded its serialized cache entry budget.", "BGCS-CACHE-BUDGET"));
                    }
                    catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                    {
                        diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "Published generation succeeded but emission cache storage failed: " + failure.Message, "BGCS-CACHE-STORE"));
                    }
            }

            void WriteJson<T>(
                string path,
                T value
            ) {
                cancellation.Token.ThrowIfCancellationRequested();
                JsonElement document = JsonSerializer.SerializeToElement(value);
                using MemoryStream bytes = new();
                using (Utf8JsonWriter writer = new(bytes, new() { Indented = true, MaxDepth = 64 }))
                    metadataNormalizer.WriteNormalized(document, writer);
                bytes.WriteByte((byte)'\n');
                byte[] content = bytes.ToArray();
                rootSink.WriteBytes(path, content);
                artifacts.Add(new(path, "application/json", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(content)))));
            }

            void RejectFormalOutputReads(SourceSnapshot snapshot)
            {
                string owned = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(logicalOutput, paths);
                string cacheOwned = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(cacheRoot, paths);
                foreach (string resolved in snapshot.files.Select(input => input.resolvedPath).Concat(snapshot.searchDirectories.Select(directory => directory.resolvedPath)).Concat(snapshot.absentInputs.Select(input => input.resolvedPath)))
                {
                    if (paths.IsWithin(resolved, owned))
                        throw new IOException("Native input resolution read a header or dependency from the formal BGCS output it would replace.");
                    if (paths.IsWithin(resolved, cacheOwned))
                        throw new IOException("Native or extension input resolution cannot read from the BGCS generation cache.");
                }
            }

        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception failure)
        {
            diagnostics.Add(failure is BGCS.CSharp.Validation.CSharpPlanningException plannedFailure ? plannedFailure.diagnostic
                : new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-GENERATION-SESSION") { details = failure.InnerException?.Message });
        }
        finally
        {
            for (int index = pendingProcessorRelease.Count - 1; index >= 0; index--) await Release(pendingProcessorRelease[index]).ConfigureAwait(false);
            for (int index = sourceContributions.Count - 1; index >= 0; index--)
                await Release(sourceContributions[index], privateStoreCleanup: published).ConfigureAwait(false);
            if (composition is not null) await Release(composition).ConfigureAwait(false);
            lock (m_gate)
                if (--m_sessions == 0 && m_closing) m_drained.TrySetResult();
        }
        return new(published, output, published ? artifacts.ToImmutable() : [], diagnostics.ToImmutable())
        {
            targetModels = targetModels.ToImmutable(),
            cacheStages = cacheEvidence.ToImmutableArray(),
            capabilities = capabilities.ToImmutableArray(),
            nativeSources = published ? nativeManifests.ToImmutableArray() : [],
            nativeInputRoots = published ? nativeInputRoots.ToImmutableArray() : []
        };

        async ValueTask ReleaseGenerationServicesAsync()
        {
            try { await composition!.DisposeGenerationServicesAsync().ConfigureAwait(false); }
            catch (Exception failure) { diagnostics.Add(new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-SESSION-RELEASE")); }
        }

        bool HasErrors()
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                BindingDiagnostic diagnostic = diagnostics[index];
                if (diagnostic.severity == BindingDiagnosticSeverity.Warning && (configuration.execution.diagnostics.warningsAsErrors
                    || diagnostic.code is { } code && diagnosticErrorCodes.Contains(code)))
                    diagnostics[index] = diagnostic with { severity = BindingDiagnosticSeverity.Error };
            }
            return diagnostics.Any(diagnostic => diagnostic.isFailure);
        }
        async ValueTask Release(
            object service,
            bool privateStoreCleanup = false
        ) {
            try
            {
                if (composition is not null && !ReferenceEquals(service, composition)) await composition.ReleaseServiceAsync(service).ConfigureAwait(false);
                else if (service is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                else if (service is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception failure)
            {
                diagnostics.Add(privateStoreCleanup
                    ? new(BindingDiagnosticSeverity.Warning, "Generation was published, but private source storage cleanup failed: " + failure.Message, "BGCS-SOURCE-CLEANUP")
                    : new(BindingDiagnosticSeverity.Error, $"Session service '{service.GetType().FullName}' failed to release: {failure.Message}", "BGCS-SESSION-RELEASE"));
            }
        }
    }

    private sealed class PrefixSink(
        IArtifactSink inner,
        string prefix
    ) : IArtifactSink
    {
        public string WriteText(
            string relativePath,
            string content
        ) => inner.WriteText(prefix + relativePath, content);
        public string WriteBytes(
            string relativePath,
            ReadOnlyMemory<byte> content
        ) => inner.WriteBytes(prefix + relativePath, content);
    }
}
