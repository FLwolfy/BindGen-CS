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
using BGCS.Contracts.Bridge;
using BGCS.Cpp2C.Planning;
using BGCS.Cpp2C.Emission;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Publishing;
using BGCS.Composition;
using BGCS.Infrastructure.Caching;

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
        => ExecuteGenerationAsync(request, compileManaged: false, cancellationToken);

    /// <summary>Generates and verifies only the C++ bridge and its C ABI contracts.</summary>
    /// <param name="request">The grouped C++ generation request.</param>
    /// <param name="cancellationToken">Cancels analysis and candidate publication.</param>
    /// <returns>The jointly published native bridge and metadata without managed emission.</returns>
    public Task<GenerationResult> BridgeAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.configuration.source.language != SourceLanguage.Cpp) throw new ArgumentException("Bridge generation requires C++ input.", nameof(request));
        return ExecuteGenerationAsync(request, compileManaged: false, cancellationToken, bridgeOnly: true);
    }

    /// <summary>Compiles candidate managed sources and performs required checks before publishing the joint artifact set.</summary>
    /// <param name="request">The immutable candidate build request.</param>
    /// <param name="cancellationToken">Cancels generation or compilation before publication.</param>
    /// <returns>The final verified candidate publication result.</returns>
    public async Task<BuildResult> BuildAsync(
        BuildRequest request,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        return new(await ExecuteGenerationAsync(request.generation, compileManaged: true, cancellationToken).ConfigureAwait(false));
    }

    private async Task<GenerationResult> ExecuteGenerationAsync(
        GenerationRequest request,
        bool compileManaged,
        CancellationToken cancellationToken,
        bool bridgeOnly = false
    ) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BindingConfiguration configuration = request.configuration;
        string logicalOutput = Path.GetFullPath(configuration.output.root, request.baseDirectory);
        string output = request.previewOutputRoot ?? logicalOutput;
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            m_sessions++;
        }
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(configuration.execution.sessionTimeoutSeconds));
        ImmutableArray<BindingDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<BindingDiagnostic>();
        HashSet<string> diagnosticErrorCodes = configuration.execution.diagnostics.errorCodes.ToHashSet(StringComparer.Ordinal);
        ImmutableArray<ArtifactDescriptor>.Builder artifacts = ImmutableArray.CreateBuilder<ArtifactDescriptor>();
        ImmutableArray<TargetInspection>.Builder targetModels = ImmutableArray.CreateBuilder<TargetInspection>();
        ImmutableArray<NativeReferenceDescriptor>.Builder nativeReferences = ImmutableArray.CreateBuilder<NativeReferenceDescriptor>();
        GenerationInputTracker extensionInputs = new(JsonSerializer.SerializeToElement(configuration), request.baseDirectory);
        List<GenerationCacheStageResult> cacheEvidence = [];
        List<(GenerationStageCache Cache, ContentDigest Key, ImmutableArray<CachedStageArtifact> Artifacts)> pendingCacheEntries = [];
        List<IArtifactProcessor> pendingProcessorRelease = [];
        List<SourceContributionCapture> sourceContributions = [];
        bool published = false;
        bool nativeBuildPerformed = false;
        GenerationComposition? composition = null;
        try
        {
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
            composition = await GenerationComposition.CreateAsync(definition, cancellation.Token, extensionInputs).ConfigureAwait(false);
            FrozenExtensionRegistry registry = composition.registry;
            BGCS.Validation.ManagedCompilationInputCapture? managedCompilation = bridgeOnly ? null
                : BGCS.Validation.ManagedCompilationInputCapture.Capture(configuration.managedBuild, request.baseDirectory, cancellation.Token);
            List<string> protectedInputs = configuration.source.headers.Concat(configuration.source.includeDirectories).Concat(configuration.source.systemIncludeDirectories)
                .Concat(configuration.nativeBuild.sources).Concat(configuration.nativeBuild.artifacts).Select(path => Path.GetFullPath(path, request.baseDirectory)).ToList();
            protectedInputs.AddRange(configuration.cpp.nativeShims.SelectMany(shim => shim.publicHeaders.Concat(shim.sourceFiles)).Select(path => Path.GetFullPath(path, request.baseDirectory)));
            protectedInputs.AddRange(configuration.managedBuild.sources.Concat(configuration.managedBuild.references).Select(path => Path.GetFullPath(path, request.baseDirectory)));
            if (managedCompilation is not null)
                protectedInputs.AddRange(managedCompilation.snapshot.files.SelectMany(input => new[] { input.path, input.resolvedPath })
                    .Concat(managedCompilation.snapshot.absentInputs.SelectMany(input => new[] { input.path, input.resolvedPath })));
            protectedInputs.Add(Path.GetFullPath(configuration.cache.directory, request.baseDirectory));
            foreach (PluginRegistration plugin in definition.Plugins)
                if (plugin.Manifest is not null)
                {
                    new BGCS.Infrastructure.Plugins.PluginManifestReader().Verify(plugin.Manifest);
                    protectedInputs.AddRange(plugin.Manifest.inputs.SelectMany(input => new[] { input.path, input.resolvedPath }));
                }
            if (request.configurationPath is not null) protectedInputs.Add(request.configurationPath);
            foreach (InputDependency input in request.configurationInputs)
            {
                protectedInputs.Add(input.path);
                protectedInputs.Add(input.resolvedPath);
                if (BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(input.path) != input.resolvedPath)
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
                string cachePhysical = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(cacheRoot);
                foreach (string input in protectedInputs.Where(input => !input.Equals(cacheRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                {
                    string sourcePhysical = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(input);
                    StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (cachePhysical.Equals(sourcePhysical, comparison) || cachePhysical.StartsWith(sourcePhysical.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)
                        || sourcePhysical.StartsWith(cachePhysical.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                        throw new IOException("The generation cache overlaps a declared source, SDK, configuration or plugin input.");
                }
                try
                {
                    reusableEmissionCache = new(cacheRoot, configuration.cache.maximumBytes, configuration.cache.maximumEntryBytes);
                    reusableEmissionCache.Prepare();
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    reusableEmissionCache = null;
                    unavailableCacheReason = failure.Message;
                    diagnostics.Add(new(BindingDiagnosticSeverity.Warning, "Emission cache preparation was unavailable: " + failure.Message, "BGCS-CACHE-UNAVAILABLE"));
                }
            }
            using ArtifactPublisher publisher = new(output, configuration.library.id, protectedInputs, cancellation.Token);
            StringComparison publicationPathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string normalizedPreviewOutput = output.Replace('\\', '/').TrimEnd('/');
            string[] normalizedCandidateOrigins = [publisher.candidateRoot.Replace('\\', '/').TrimEnd('/'),
                Path.GetRelativePath(request.baseDirectory, publisher.candidateRoot).Replace('\\', '/').TrimEnd('/')];
            FrontendGenerationCache primaryFrontendCache = new(reusableEmissionCache, unavailableCacheReason, extensionInputs, cacheEvidence, pendingCacheEntries);
            DirectoryArtifactSink rootSink = new(publisher.candidateRoot);
            HashSet<string> concreteTargets = new(StringComparer.Ordinal);
            List<SourceSnapshot> parsedInputs = [];
            foreach (TargetConfiguration target in configuration.targets)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                SourceConfiguration source = configuration.source;
                BGCS.Cpp2C.Shims.NativeShimPlanner shimPlanner = new();
                ImmutableArray<NativeShimPlan> shims = configuration.cpp.nativeShims.Select(shim => shimPlanner.Plan(shim, request.baseDirectory)).ToImmutableArray();
                ImmutableArray<string> parseHeaders = source.headers.Select(path => Path.GetFullPath(path, request.baseDirectory))
                    .Concat(shims.SelectMany(shim => shim.publicHeaders)).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray();
                TemplateInstantiationPlanner templatePlanner = new();
                TemplateInstantiationPlan? templates = source.language == SourceLanguage.Cpp
                    && (!configuration.cpp.templateInstantiations.types.IsEmpty || !configuration.cpp.templateInstantiations.functions.IsEmpty)
                    ? templatePlanner.Plan(configuration.cpp.templateInstantiations) : null;
                string nativePostamble = source.postamble + (templates is null ? string.Empty : "\n" + templates.postamble);
                if (source.language == SourceLanguage.Cpp && !configuration.cpp.sequenceInputAdapters.IsEmpty)
                    nativePostamble += StandardPathInputProbe.postamble;
                AnalysisRequest analysisRequest = new(parseHeaders, source.language, request.baseDirectory)
                {
                    targetId = target.id,
                    triple = target.triple,
                    languageStandard = source.languageStandard!,
                    includeDirectories = source.includeDirectories,
                    systemIncludeDirectories = source.systemIncludeDirectories,
                    defines = source.defines,
                    arguments = source.arguments,
                    preamble = source.preamble,
                    postamble = nativePostamble,
                    parseMacros = source.parseMacros,
                    parseComments = source.parseComments,
                    parseSystemIncludes = source.includeSystemDeclarations,
                    snapshotExcludedDirectories = new[] { publisher.candidateRoot, output, logicalOutput, cacheRoot }.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(),
                    nativeCompilerFamily = configuration.nativeBuild.provider is "clang" or "gnu" or "msvc" or "clang-cl" or "msbuild"
                        ? configuration.nativeBuild.provider : null,
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
                        contribution = await SourceContributionCapture.CaptureAsync(registry, configuration, prepared, extensionInputs, !bridgeOnly, token).ConfigureAwait(false);
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
                NativeOutputKind nativeOutputKind = configuration.nativeBuild.outputKind ?? BGCS.Platforms.Capabilities.PlatformCapabilityCatalog.Get(analysis.target!).defaultOutputKind;
                ManagedRuntimeProfile runtimeProfile = BGCS.Platforms.ManagedRuntimeResolver.Resolve(analysis.target!, configuration.api.runtime
                    ?? (analysis.target!.platformId == "emscripten" ? ManagedRuntimeKind.BrowserWasm
                        : nativeOutputKind is NativeOutputKind.StaticLibrary or NativeOutputKind.Object || analysis.target.platformId is "ios" or "ios-simulator"
                            ? ManagedRuntimeKind.NativeAot : ManagedRuntimeKind.CoreClr), nativeOutputKind);
                string? nativeArtifactFileName = configuration.nativeBuild.mode == "none" ? null
                    : BGCS.Platforms.Assets.NativeOutputLayout.ResolveFilename(configuration.library.nativeName, analysis.target!, nativeOutputKind);
                string emittedLibraryName = BGCS.Platforms.Assets.NativeOutputLayout.ResolveImportModuleName(
                    analysis.target!, runtimeProfile, configuration.library.nativeName, nativeArtifactFileName);
                if (!concreteTargets.Add(targetId)) throw new InvalidOperationException($"Two target selections resolve to '{targetId}'.");
                ImmutableArray<NativeGeneratedInputMap> contributionMaps = contribution?.Mappings(targetId) ?? [];
                if (contribution is not null) artifacts.AddRange(contribution.PublishNative(rootSink, targetId));
                NativeModule originalModule = analysis.module!;
                HashSet<StableId> ordinaryDeclarations = originalModule.declarations.Select(declaration => declaration.id).ToHashSet();
                StableId? pathInputProbe = source.language == SourceLanguage.Cpp && !configuration.cpp.sequenceInputAdapters.IsEmpty
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
                ImmutableArray<CppVirtualProxyPlan> virtualProxies = configuration.cpp.virtualCallbacks.Select(callback =>
                    new VirtualCallbackPlanner().Plan(originalModule, callback)).ToImmutableArray();
                NativeVariadicAdapterPlanner variadicPlanner = new();
                NativeVariadicAdapterPlan variadicAdapters = variadicPlanner.Plan(originalModule, source.variadicVariants);
                if (!virtualProxies.IsEmpty || !variadicAdapters.adapters.IsEmpty)
                {
                    nativePostamble += "\n" + string.Join("\n", virtualProxies.Select(proxy => proxy.source.text)) + "\n" + variadicAdapters.source.text;
                    analysisRequest = analysisRequest with { postamble = nativePostamble, retainFunctionBodies = !variadicAdapters.adapters.IsEmpty };
                    AnalysisResult expanded = await AnalyzeCoreAsync(analysisRequest, applyTransforms: true, cancellation.Token, composition, extensionInputs, primaryFrontendCache).ConfigureAwait(false);
                    diagnostics.AddRange(expanded.diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));
                    if (!expanded.success) break;
                    RejectFormalOutputReads(expanded.snapshot!);
                    parsedInputs.Add(expanded.snapshot!);
                    if (expanded.target != analysis.target && (expanded.module!.targetId != analysis.module.targetId || expanded.module.triple != analysis.module.triple))
                        throw new InvalidOperationException("Generated native adapter reparse changed the original native target.");
                    analysis = expanded;
                    HashSet<string> factories = virtualProxies.Select(proxy => proxy.factory).ToHashSet(StringComparer.Ordinal);
                    ImmutableArray<StableId> proxyFactories = analysis.module!.declarations.Where(declaration => declaration.kind == "Function" && factories.Contains(declaration.qualifiedName))
                        .Select(declaration => declaration.id).ToImmutableArray();
                    if (proxyFactories.Length != virtualProxies.Length)
                        throw new InvalidOperationException("Virtual callback reparse did not produce each exact generated factory.");
                    foreach (StableId factory in proxyFactories) requestedPublicDeclarations.Add(factory.value);
                    foreach (StableId wrapper in variadicPlanner.VerifyReparse(analysis.module!, variadicAdapters)) requestedPublicDeclarations.Add(wrapper.value);
                }
                HashSet<string> requiredDeclarations = configuration.cpp.typeLowerings.Where(recipe => recipe.ownedResultRelease is not null)
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
                DeclarationSelection selection = new DeclarationSelector().Select(analysis.module!, source.selection, source.headers.Concat(contribution?.exposedHeaders ?? []).ToImmutableArray(), request.baseDirectory,
                    source.includeTransitivelyReferencedHeaders, source.includeSystemDeclarations, requiredDeclarations, cancellation.Token,
                    additionalPublicDeclarations: requestedPublicDeclarations, ordinaryDeclarations: ordinaryDeclarations,
                    requiredMetadataDeclarations: virtualProxies.SelectMany(proxy => proxy.source.declarationDependencies)
                        .Concat(variadicAdapters.source.declarationDependencies).Select(id => id.value), includeCHeaderHelpers: source.includeHeaderHelpers,
                    requiredNativeTypes: pathInputProbe is StableId pathUnit ? [StandardLibraryFactReader.Resolve(analysis.module!, analysis.module!.GetDeclaration(pathUnit).type!.Value).id] : []);
                NativeModule selectedModule = selection.module;
                HashSet<StableId> adaptedVariadics = variadicAdapters.adapters.Select(adapter => adapter.sourceDeclaration).ToHashSet();
                ImmutableArray<StableId> selectedCallables = selection.callables.Where(id => !adaptedVariadics.Contains(id)).ToImmutableArray();
                if (!variadicAdapters.adapters.IsEmpty) WriteJson(targetId + "/native/metadata/variadic.adapters.json", variadicAdapters);
                InteropModule interop;
                CppBridgeModule? bridgeModel = null;
                CHeaderBridgeModule? cHeaderBridge = null;
                SourceSnapshot? bridgeSnapshot = null;
                object? bridgeInputEvidence = null;
                if (source.language == SourceLanguage.Cpp)
                {
                    string prefix = configuration.library.bridgeExportPrefix ?? "bgcs_" + Regex.Replace(configuration.library.id, "[^A-Za-z0-9]+", "_", RegexOptions.CultureInvariant).Trim('_');
                    List<object> loweringServices = [];
                    CppBridgeModule bridge;
                    try
                    {
                        List<ICppTypeLowering> typeLowerings = [];
                        foreach (TypeLoweringRecipe recipe in configuration.cpp.typeLowerings)
                            typeLowerings.Add(new BGCS.Cpp2C.Recipes.LoweringRecipeCompiler().Compile(recipe with
                            { sourceFragments = recipe.sourceFragments.Select(fragment => fragment with { requiredHeaders = fragment.requiredHeaders.Select(path => Path.GetFullPath(path, request.baseDirectory)).ToImmutableArray() }).ToImmutableArray() }));
                        List<ICppCallableLowering> callableLowerings = [];
                        foreach (CppTypeRegistration registration in registry.CppTypes)
                        {
                            ICppTypeLowering service = registration.Factory() ?? throw new InvalidOperationException("A C++ type lowering factory returned no service.");
                            loweringServices.Add(service);
                            extensionInputs.Capture(service, analysis.target!, cancellation.Token);
                            if (service.id != registration.Id || service.priority != registration.Priority) throw new InvalidOperationException("A C++ type lowering changed its frozen factory registration.");
                            typeLowerings.Add(service);
                        }
                        foreach (CppCallableRegistration registration in registry.CppCallables)
                        {
                            ICppCallableLowering service = registration.Factory() ?? throw new InvalidOperationException("A C++ callable lowering factory returned no service.");
                            loweringServices.Add(service);
                            extensionInputs.Capture(service, analysis.target!, cancellation.Token);
                            if (service.id != registration.Id || service.priority != registration.Priority) throw new InvalidOperationException("A C++ callable lowering changed its frozen factory registration.");
                            callableLowerings.Add(service);
                        }
                        bridge = new CppBridgePlanner(new LoweringRegistry(typeLowerings, callableLowerings), configuration.cpp.callableLowerings.Select(recipe => recipe with
                        { sourceFragments = recipe.sourceFragments.Select(fragment => fragment with { requiredHeaders = fragment.requiredHeaders.Select(path => Path.GetFullPath(path, request.baseDirectory)).ToImmutableArray() }).ToImmutableArray() }), configuration.cpp.familyBindings).Plan(new(selectedModule, analysis.target!, prefix,
                            parseHeaders), selectedCallables, cancellation.Token, publicDeclarations: selection.publicDeclarations, virtualProxies: virtualProxies) with
                        {
                            preamble = source.preamble,
                            postamble = nativePostamble,
                            headers = parseHeaders.Select(path => NativeGeneratedInputRelocation.HeaderInclude(path, contributionMaps, targetId + "/native/src")).ToImmutableArray()
                        };
                    }
                    finally
                    {
                        for (int index = loweringServices.Count - 1; index >= 0; index--) await Release(loweringServices[index]).ConfigureAwait(false);
                    }
                    if (HasErrors()) break;
                    bridge = new SequenceInputAdapterPlanner().Apply(bridge, configuration.cpp.sequenceInputAdapters, cancellation.Token);
                    new CppBridgeLanguageValidator().Validate(bridge, source.languageStandard!);
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
                    bridgeInputEvidence = NativeSourceInputIdentity.Evidence(bridgeSnapshot!, reparsed.preparedFrontend!.sourcePathMappings);
                    parsedInputs.Add(bridgeSnapshot!);
                    WriteJson(nativePrefix + "metadata/bridge.module.json", bridge);
                }
                else
                {
                    string prefix = configuration.library.bridgeExportPrefix ?? "bgcs_" + Regex.Replace(configuration.library.id, "[^A-Za-z0-9]+", "_", RegexOptions.CultureInvariant).Trim('_');
                    cHeaderBridge = new CHeaderBridgePlanner().Plan(selectedModule, selectedCallables, prefix);
                    if (!cHeaderBridge.exports.IsEmpty)
                    {
                        string nativePrefix = targetId + "/native/";
                        ImmutableArray<string> nativeHeaders = parseHeaders.Select(path => NativeGeneratedInputRelocation.HeaderInclude(path, contributionMaps, nativePrefix + "include")).ToImmutableArray();
                        ImmutableArray<ArtifactDescriptor> native = new CHeaderBridgeEmitter().Emit(cHeaderBridge, selectedModule, nativeHeaders,
                            new PrefixSink(rootSink, nativePrefix), source.preamble, nativePostamble, cancellation.Token);
                        artifacts.AddRange(native.Select(artifact => artifact with { relativePath = nativePrefix + artifact.relativePath }));
                        AnalysisResult reparsed = await AnalyzeCoreAsync(analysisRequest with
                        {
                            headers = [Path.Combine(publisher.candidateRoot, targetId, "native", "include", prefix + "_helpers.h")],
                            sourcePathMappings = analysisRequest.sourcePathMappings.SetItem(Path.Combine(publisher.candidateRoot, targetId, "native", "include", prefix + "_helpers.h"),
                                targetId + "/native/include/" + prefix + "_helpers.h"),
                            language = SourceLanguage.C,
                            languageStandard = source.languageStandard!,
                            preamble = string.Empty,
                            postamble = string.Empty,
                            parseMacros = false,
                            parseComments = false,
                            parseSystemIncludes = false
                        }, applyTransforms: false, cancellation.Token, composition, extensionInputs).ConfigureAwait(false);
                        diagnostics.AddRange(reparsed.diagnostics);
                        if (!reparsed.success) break;
                        new CHeaderBridgeVerifier().Verify(selectedModule, cHeaderBridge, reparsed.module!);
                        bridgeSnapshot = reparsed.snapshot;
                        bridgeInputEvidence = NativeSourceInputIdentity.Evidence(bridgeSnapshot!, reparsed.preparedFrontend!.sourcePathMappings);
                        parsedInputs.Add(bridgeSnapshot!);
                        WriteJson(nativePrefix + "metadata/header-helpers.manifest.json", cHeaderBridge);
                    }
                    interop = new CDeclarationAnalyzer().Analyze(selectedModule, configuration.library.id, selectedCallables, cHeaderBridge);
                }
                interop = new NativeConstantAnalyzer().Apply(selectedModule, interop, selection.publicDeclarations);
                diagnostics.AddRange(interop.diagnostics.Where(diagnostic => !analysis.diagnostics.Contains(diagnostic)));
                if (HasErrors()) break;
                interop = ConfigurationSemantics.Apply(interop, configuration, request.configurationPath ?? "sdk-configuration");
                diagnostics.AddRange(interop.diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));
                if (HasErrors()) break;
                IManagedProjection? projection = null;
                IBindingBackend? backend = null;
                List<IInteropTransform> transforms = [];
                List<IManagedApiTransform> managedTransforms = [];
                List<(ProcessorRegistration Registration, IArtifactProcessor Service)> preparedProcessors = [];
                ImmutableArray<ArtifactDescriptor> emitted = [];
                ManagedApiModel? managedModel = null;
                BGCS.CSharp.Planning.CSharpEmissionPlan? emittedPlan = null;
                StaticExportTableDescriptor? staticTable = null;
                try
                {
                    InteropModule baseline = interop;
                    AnalysisContext transformContext = new(analysis.target!, configuration.source.language, analysis.snapshot!);
                    foreach (InteropTransformRegistration registration in registry.InteropTransforms)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        IInteropTransform transform = registration.Factory() ?? throw new InvalidOperationException($"Interoperability transformation '{registration.Id}' has no service.");
                        transforms.Add(transform);
                        extensionInputs.Capture(transform, analysis.target!, cancellation.Token);
                        if (transform.id != registration.Id || transform.priority != registration.Priority)
                            throw new InvalidOperationException($"Interoperability transformation '{registration.Id}' changed its registration identity.");
                        interop = transform.Transform(interop, transformContext, cancellation.Token) ?? throw new InvalidOperationException("An interoperability transformation returned no model.");
                        if (transform.id != registration.Id || transform.priority != registration.Priority)
                            throw new InvalidOperationException($"Interoperability transformation '{registration.Id}' changed its frozen identity during execution.");
                        new InteropModuleVerifier().VerifyPreservedAbi(baseline, interop);
                        diagnostics.AddRange(interop.diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));
                        if (HasErrors()) break;
                    }
                    if (HasErrors()) break;
                    new BGCS.Analysis.Bridge.NativeValueProtocolVerifier().Verify(interop);
                    diagnostics.AddRange(new BGCS.Analysis.Safety.SafetyAnalyzer().Analyze(interop).Where(diagnostic => !diagnostics.Contains(diagnostic)));
                    if (HasErrors()) break;
                    if (runtimeProfile.moduleMechanism == NativeModuleMechanism.StaticAdapter && configuration.api.importMode == BindingImportMode.FunctionTable)
                    {
                        var adapter = BGCS.NativeBuild.Planning.StaticExportTableEmitter.Emit(interop,
                            bridgeModel is null ? parseHeaders.Select(path => NativeGeneratedInputRelocation.HeaderInclude(path, contributionMaps, targetId + "/native/src")).ToImmutableArray() : [bridgeModel.exportPrefix + ".h"]);
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
                        if (bridgeModel is not null) model = new BGCS.CSharp.Projection.ManagedRecipeProjectionBinder().Apply(model, bridgeModel, configuration.cpp.typeLowerings);
                        foreach (ManagedTransformRegistration registration in registry.ManagedTransforms)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            IManagedApiTransform transform = registration.Factory()
                                ?? throw new InvalidOperationException($"Managed transformation '{registration.Id}' has no service.");
                            managedTransforms.Add(transform);
                            extensionInputs.Capture(transform, analysis.target!, cancellation.Token);
                            if (transform.id != registration.Id || transform.priority != registration.Priority)
                                throw new InvalidOperationException($"Managed transformation '{registration.Id}' changed its frozen registration identity.");
                            model = transform.Transform(model, transformContext, cancellation.Token)
                                ?? throw new InvalidOperationException("A managed transformation returned no model.");
                            if (transform.id != registration.Id || transform.priority != registration.Priority)
                                throw new InvalidOperationException($"Managed transformation '{registration.Id}' changed its frozen identity during execution.");
                            if (!ReferenceEquals(model.interop, interop))
                                throw new InvalidOperationException("A managed transformation changed the frozen native ABI instead of its presentation.");
                        }
                        var externalProjection = await BGCS.Validation.ManagedExternalProjectionBinder.BindAsync(model, configuration, request.baseDirectory, cancellation.Token).ConfigureAwait(false);
                        model = externalProjection.Model;
                        if (externalProjection.Inputs is not null) extensionInputs.CaptureArgumentInputs(externalProjection.Inputs);
                        managedModel = model;
                        string runtimeNamespace = configuration.runtime.mode == "source" ? configuration.runtime.@namespace ?? configuration.api.@namespace + ".Runtime" : "BGCS.Runtime";
                        if (configuration.runtime.mode == "package" && configuration.runtime.@namespace is not null && configuration.runtime.@namespace != "BGCS.Runtime")
                            throw new ArgumentException("Package runtime references require the BGCS.Runtime namespace.");
                        managedCompilation?.Verify(cancellation.Token);
                        EmissionRequest emission = new(emittedLibraryName, new PrefixSink(rootSink, targetId + "/managed/"))
                        {
                            framework = configuration.api.framework,
                            maximumValueStringCodeUnits = configuration.api.maximumValueStringCodeUnits,
                            importMode = configuration.api.importMode,
                            additionalUsings = configuration.api.usings,
                            managedRuntime = runtimeProfile,
                            staticExportTable = staticTable,
                            runtimeNamespace = runtimeNamespace,
                            includeRuntimeSource = configuration.runtime.mode == "source",
                            additionalRuntimeSourceFeatures = configuration.runtime.sourceFeatures,
                            singleFile = configuration.output.layout == "singleFile",
                            singleFileName = configuration.output.singleFileName,
                            managedCompilation = managedCompilation?.inputs
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
                        foreach (ProcessorRegistration registration in ConfiguredSourceProcessors.Prepare(configuration.output.processors, registry.Processors))
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            IArtifactProcessor processor = registration.Factory() ?? throw new InvalidOperationException("A source processor factory returned no service.");
                            pendingProcessorRelease.Add(processor);
                            preparedProcessors.Add((registration, processor));
                            extensionInputs.Capture(processor, analysis.target!, cancellation.Token);
                            if (processor.id != registration.Id || processor.priority != registration.Priority)
                                throw new InvalidOperationException("A processor changed its frozen identity before emission.");
                        }
                        managedCompilation?.Verify(cancellation.Token);
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
                                stageKey = EmissionStageCache.CreateKey(model, emission, configuration, extensionInputs, definition, cancellation.Token);
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
                                    (artifact.descriptor.relativePath, new UTF8Encoding(false, true).GetString(artifact.content.Span))), emittedLibraryName, cancellation.Token);
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
                targetModels.Add(new(targetId, selectedModule, bridgeModel, interop, managedModel)
                {
                    resolvedTarget = analysis.target,
                    extensionNativeBuildProviders = registry.NativeBuildProviders.Select(provider => provider.Id).Order(StringComparer.Ordinal).ToImmutableArray()
                });
                if (!bridgeOnly && (preparedProcessors.Count != 0 || contribution is not null && configuration.output.layout == "singleFile"))
                {
                    if (emittedPlan is null && registry.Validators.IsDefaultOrEmpty) throw new NotSupportedException("A custom backend with source processors requires its own final artifact validation gate.");
                    using CandidateTextEditor editor = new(publisher.candidateRoot, emitted.Select(artifact => artifact with { relativePath = targetId + "/managed/" + artifact.relativePath }), targetId + "/managed/");
                    try
                    {
                        foreach ((ProcessorRegistration registration, IArtifactProcessor processor) in preparedProcessors)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            if (processor.id != registration.Id || processor.priority != registration.Priority) throw new InvalidOperationException("A processor changed its frozen identity.");
                            await processor.ProcessAsync(new(managedModel!, editor.artifacts, editor), cancellation.Token).ConfigureAwait(false);
                            if (processor.id != registration.Id || processor.priority != registration.Priority) throw new InvalidOperationException("A processor changed its identity during execution.");
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
                            new BGCS.CSharp.Validation.EmittedInteropContractReader().Verify(emittedPlan, actual, emittedLibraryName, cancellation.Token);
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
                artifacts.AddRange(emitted.Select(artifact => artifact with { relativePath = targetId + "/managed/" + artifact.relativePath }));
                if (compileManaged)
                {
                    if (emitted.Any(artifact => artifact.relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && artifact.mediaType != "text/x-csharp"))
                        throw new InvalidOperationException("A C# candidate artifact has an inconsistent managed media type.");
                    var compilation = await new BGCS.Validation.ManagedCompilationValidator().ValidateAsync(emitted.Where(artifact => artifact.mediaType == "text/x-csharp")
                        .Select(artifact => Path.Combine(publisher.candidateRoot, targetId, "managed", artifact.relativePath)), configuration, request.baseDirectory, cancellation.Token).ConfigureAwait(false);
                    if (compilation.exitCode != 0 || compilation.timedOut)
                    {
                        diagnostics.Add(new(BindingDiagnosticSeverity.Error, compilation.standardOutput + Environment.NewLine + compilation.standardError, "BGCS-MANAGED-COMPILE"));
                        break;
                    }
                }
                bool nativeLinked = false;
                object? nativeEvidence = null;
                NativeAssetPackageResult? nativeAssets = null;
                BindingConfiguration nativeConfiguration = shims.IsEmpty ? configuration : configuration with
                { nativeBuild = configuration.nativeBuild with { sources = configuration.nativeBuild.sources.Concat(shims.SelectMany(shim => shim.sourceFiles)).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray() } };
                if (source.language == SourceLanguage.Cpp || configuration.nativeBuild.mode == "sources" || staticTable is not null || cHeaderBridge is { exports.IsEmpty: false })
                {
                    ImmutableArray<string> nativeHeaders = parseHeaders.Select(path => NativeGeneratedInputRelocation.HeaderInclude(path, contributionMaps, targetId + "/native/src")).ToImmutableArray();
                    string assertionPath = targetId + "/native/src/storage.assertions." + (source.language == SourceLanguage.C ? "c" : "cpp");
                    string assertions = new BGCS.NativeBuild.Planning.NativeStorageAssertionEmitter().Emit(selectedModule,
                        nativeHeaders, source.preamble, nativePostamble);
                    rootSink.WriteText(assertionPath, assertions);
                    artifacts.Add(new(assertionPath, source.language == SourceLanguage.C ? "text/x-c" : "text/x-c++",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(assertions))))));
                    string factsPath = targetId + "/native/src/facts.input." + (source.language == SourceLanguage.C ? "h" : "hpp");
                    string factsInput = new BGCS.NativeBuild.Planning.NativeFactInputEmitter().Emit(nativeHeaders, source.preamble, nativePostamble, selectedModule);
                    rootSink.WriteText(factsPath, factsInput);
                    artifacts.Add(new(factsPath, source.language == SourceLanguage.C ? "text/x-c-header" : "text/x-c++-header",
                        new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(factsInput))))));
                    NativeGeneratedInputRelocation generatedInputs = new(contributionMaps, artifacts.ToImmutable(), publisher.candidateRoot);
                    var nativeManifest = NativeBuildManifestFactory.Create(nativeConfiguration, analysis.target!, analysis.snapshot!, artifacts.ToImmutable(), request.baseDirectory, target.settings.defines, definition, generatedInputs, analysis.preparedFrontend)
                        with
                    {
                        managedRuntime = runtimeProfile,
                        importModuleName = emittedLibraryName,
                        nativeArtifactFilename = nativeArtifactFileName ?? (runtimeProfile.kind == ManagedRuntimeKind.BrowserWasm
                                ? emittedLibraryName + (nativeOutputKind == NativeOutputKind.Object ? ".o" : ".a")
                                : BGCS.Platforms.Assets.NativeOutputLayout.ResolveFilename(configuration.library.nativeName, analysis.target!, nativeOutputKind)),
                        interop = interop,
                        sourceFacts = selectedModule,
                        factVerificationSources = ["artifact:/" + factsPath],
                        extensionInputs = extensionInputs.snapshots.Select(snapshot => generatedInputs.Relocate(contribution?.NativeInputs(snapshot) ?? snapshot)).ToImmutableArray(),
                        requiredExports = interop.functions.Select(function => function.exportName).Concat(staticTable is null ? [] : new[]
                            { staticTable.getExportSymbol, staticTable.getRevisionSymbol, staticTable.getCountSymbol, staticTable.getPointerSizeSymbol, staticTable.getAbiIdentitySymbol }).ToImmutableArray()
                    };
                    parsedInputs.Add(NativeBuildManifestFactory.CandidateExternalInputs(nativeManifest, publisher.candidateRoot));
                    WriteJson(targetId + "/native/metadata/bridge.manifest.json", nativeManifest);
                }
                if (compileManaged && configuration.nativeBuild.mode != "none")
                {
                    if (configuration.nativeBuild.mode == "prebuilt" && cHeaderBridge is { exports.IsEmpty: false })
                    {
                        string binary = Path.GetFullPath(configuration.nativeBuild.artifacts.Single(), request.baseDirectory);
                        var helperExports = await BGCS.NativeBuild.NativeExportInspector.InspectAsync(analysis.target!, binary,
                            cHeaderBridge.exports.Select(export => export.exportName), cancellation.Token, outputKind: nativeOutputKind).ConfigureAwait(false);
                        if (!helperExports.success)
                        {
                            diagnostics.Add(new(BindingDiagnosticSeverity.Error,
                                $"The prebuilt native module '{configuration.library.nativeName}' does not contain the selected C header helper adapters. Compile '{targetId}/native/src/{cHeaderBridge.exportPrefix}_helpers.c' into that same module, or use nativeBuild.mode=sources with the declared upstream inputs. Missing adapter exports: {string.Join(", ", helperExports.missing)}.",
                                "BGCS-C-INTERNAL-LINKAGE")
                            { location = selectedModule.GetDeclaration(cHeaderBridge.exports[0].sourceDeclaration).source });
                            break;
                        }
                    }
                    var nativeResult = await new NativeCandidateBuilder().BuildAsync(nativeConfiguration, analysis.target!, interop,
                        artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/src/", StringComparison.Ordinal) && artifact.mediaType is "text/x-c++" or "text/x-c")
                            .Select(artifact => Path.Combine(publisher.candidateRoot, artifact.relativePath)),
                        artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/", StringComparison.Ordinal) && artifact.mediaType == "text/x-c" && artifact.relativePath.EndsWith(".h", StringComparison.Ordinal))
                            .Select(artifact => Path.GetDirectoryName(Path.Combine(publisher.candidateRoot, artifact.relativePath))!)
                            .Concat(contribution?.includeDirectories.Select(_ => Path.Combine(publisher.candidateRoot, targetId, "native", "include")) ?? []),
                        target.settings.defines, request.baseDirectory, rootSink, targetId + "/native/bin", cancellation.Token,
                        registry.NativeBuildProviders.SingleOrDefault(provider => provider.Id == configuration.nativeBuild.provider)?.Factory,
                        new[] { publisher.candidateRoot, output, logicalOutput, cacheRoot }.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray(), extensionInputs,
                        interop.functions.Select(function => function.exportName).Concat(staticTable is null ? [] : new[]
                            { staticTable.getExportSymbol, staticTable.getRevisionSymbol, staticTable.getCountSymbol, staticTable.getPointerSizeSymbol, staticTable.getAbiIdentitySymbol }).ToImmutableArray(),
                        selectedModule, registry.Frontend,
                        artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/src/", StringComparison.Ordinal) && artifact.mediaType is "text/x-c-header" or "text/x-c++-header")
                            .Select(artifact => Path.Combine(publisher.candidateRoot, artifact.relativePath)).ToImmutableArray(),
                        new NativeGeneratedInputRelocation(contributionMaps, artifacts.ToImmutable(), publisher.candidateRoot).SourcePathMappings(publisher.candidateRoot), analysis.preparedFrontend).ConfigureAwait(false);
                    artifacts.Add(nativeResult.artifact);
                    nativeLinked = nativeResult.linked;
                    nativeBuildPerformed = true;
                    nativeEvidence = nativeResult.evidence;
                    NativeReferenceDescriptor nativeReference = BGCS.Platforms.Assets.NativeReferenceCatalog.Create(analysis.target!, runtimeProfile, nativeOutputKind, nativeResult.artifact, configuration.library.nativeName);
                    if (nativeReference.importModuleName != emittedLibraryName)
                        throw new InvalidOperationException("The native consumption reference changed the generated import module identity.");
                    nativeReferences.Add(nativeReference);
                    RejectFormalOutputReads(nativeResult.inputSnapshot);
                    parsedInputs.Add(nativeResult.inputSnapshot);
                    RejectFormalOutputReads(nativeResult.argumentInputs);
                    parsedInputs.Add(nativeResult.argumentInputs);
                    if (configuration.nativeBuild.packaging.enabled)
                    {
                        var package = await new NativePackageCandidateBuilder().PrepareAsync(configuration, analysis.target!, nativeResult.artifact,
                            publisher.candidateRoot, rootSink, registry, extensionInputs, cancellation.Token).ConfigureAwait(false);
                        artifacts.AddRange(package.artifacts);
                        nativeAssets = package.assets;
                    }
                }
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
                    managedAggregateProfile = emittedPlan?.managedRuntime?.aggregateAbi,
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
                    ValidationRequest validation = new(analysis.target!, interop, candidates, new DirectoryArtifactReader(publisher.candidateRoot, candidates))
                    { managedCompiled = compileManaged, nativeLinked = nativeLinked, nativeArtifactVerified = nativeEvidence is not null };
                    foreach (ValidatorRegistration registration in registry.Validators)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        IGenerationValidator validator = registration.Factory() ?? throw new InvalidOperationException("A validation factory returned no gate.");
                        validators.Add(validator);
                        extensionInputs.Capture(validator, analysis.target!, cancellation.Token);
                        if (validator.id != registration.Id || validator.priority != registration.Priority) throw new InvalidOperationException("A validator changed its frozen registration identity.");
                        ImmutableArray<BindingDiagnostic> checks = await validator.ValidateAsync(validation, cancellation.Token).ConfigureAwait(false);
                        if (checks.IsDefault || checks.Any(diagnostic => diagnostic is null || !Enum.IsDefined(diagnostic.severity))) throw new InvalidOperationException("A validator returned an incomplete or invalid diagnostic collection.");
                        if (validator.id != registration.Id || validator.priority != registration.Priority) throw new InvalidOperationException("A validator changed its identity during validation.");
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
                    managedCompiled = compileManaged,
                    nativeLinked,
                    nativeArtifactVerified = nativeEvidence is not null,
                    nativeEvidence,
                    invoked = false,
                    packaged = false,
                    managedRuntime = runtimeProfile,
                    staticExportTable = staticTable,
                    assetsPrepared = nativeAssets is not null,
                    nativeAssets,
                    nativeReferences = nativeReferences.Where(reference => reference.target.targetId.value == targetId).ToImmutableArray(),
                    implementation = typeof(BindingHost).Assembly.ManifestModule.ModuleVersionId.ToString(),
                    cache = cacheEvidence.Where(evidence => evidence.targetId == targetId).ToArray(),
                    inputs = analysis.snapshot,
                    bridgeInputs = bridgeInputEvidence,
                    configurationInputs = request.configurationInputs,
                    configurationRemoteInputs = request.configurationRemoteInputs,
                    managedCompilationInputs = managedCompilation?.snapshot,
                    managedCompilationEnvironment = managedCompilation is null ? null : new
                    {
                        assemblies = managedCompilation.inputs.assemblies.Select(input => new
                        { input.path, input.assemblyIdentity, input.digest, length = input.image.Length, input.isFramework }).ToArray(),
                        managedCompilation.inputs.defines
                    },
                    extensionInputs = extensionInputs.snapshots,
                    nativeSourceInputs = extensionInputs.nativeSourceEvidence,
                    plugins = definition.Plugins.Select(plugin => new { descriptor = plugin.Descriptor, settings = plugin.Configuration?.settings, pathBase = plugin.Configuration?.defaultPathBase, valuePathBases = plugin.Configuration?.valuePathBases, inputs = plugin.Manifest?.inputs, absentInputs = plugin.Manifest?.missingInputs }).ToArray(),
                    validationGates = registry.Validators.Select(validator => new { id = validator.Id, priority = validator.Priority }).ToArray()
                });

            }
            for (int index = pendingProcessorRelease.Count - 1; index >= 0; index--) await Release(pendingProcessorRelease[index]).ConfigureAwait(false);
            pendingProcessorRelease.Clear();
            await Release(composition).ConfigureAwait(false);
            composition = null;
            if (!HasErrors())
            {
                managedCompilation?.Verify(cancellation.Token);
                await extensionInputs.VerifyAsync(cancellation.Token).ConfigureAwait(false);
                foreach (SourceContributionCapture capture in sourceContributions) capture.Verify();
                foreach (SourceSnapshot external in extensionInputs.snapshots)
                {
                    RejectFormalOutputReads(external);
                    string destination = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(logicalOutput).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    foreach (DirectoryInputDependency directory in external.searchDirectories)
                    {
                        string sourceDirectory = directory.resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (destination.Equals(sourceDirectory, comparison) || destination.StartsWith(sourceDirectory + Path.DirectorySeparatorChar, comparison))
                            throw new IOException("A declared extension input directory overlaps the formal output it would replace.");
                    }
                }
                foreach (SourceSnapshot snapshot in parsedInputs)
                    if (BGCS.Infrastructure.IO.SourceSnapshotVerifier.GetMismatch(snapshot, cancellation.Token) is string mismatch)
                        throw new IOException("Native file bytes or include-search membership changed during generation; the candidate cannot be published. " + mismatch);
                foreach (InputDependency input in request.configurationInputs)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(input.path) != input.resolvedPath) throw new IOException("A composed configuration input changed its physical identity before publication.");
                    using FileStream stream = File.OpenRead(input.path);
                    if (stream.Length != input.length || new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))) != input.digest)
                        throw new IOException("A composed configuration input changed during generation; the candidate cannot be published.");
                }
                foreach (PluginRegistration plugin in definition.Plugins)
                    if (plugin.Manifest is not null) new BGCS.Infrastructure.Plugins.PluginManifestReader().Verify(plugin.Manifest);
                managedCompilation?.Verify(cancellation.Token);
                WriteJson("generation.report.json", new
                {
                    schemaVersion = 1,
                    libraryId = configuration.library.id,
                    targets = concreteTargets.Order(StringComparer.Ordinal).ToArray(),
                    diagnostics = diagnostics.ToImmutable(),
                    cache = cacheEvidence,
                    nativeBuildPerformed,
                    managedCompilationPerformed = compileManaged,
                    bridgeOnly
                });
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
                JsonNode? document = JsonSerializer.SerializeToNode(value, value is BGCS.Contracts.Requests.NativeBuildManifest
                    ? BGCS.Infrastructure.Workers.WorkerProtocol.jsonOptions : null);
                NormalizeCandidatePaths(document);
                string text = (document?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null") + "\n";
                rootSink.WriteText(path, text);
                artifacts.Add(new(path, "application/json", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))))));
            }

            void RejectFormalOutputReads(SourceSnapshot snapshot)
            {
                string owned = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(logicalOutput).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string cacheOwned = BGCS.Infrastructure.IO.PhysicalPathResolver.Resolve(cacheRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                foreach (string resolved in snapshot.files.Select(input => input.resolvedPath).Concat(snapshot.searchDirectories.Select(directory => directory.resolvedPath)).Concat(snapshot.absentInputs.Select(input => input.resolvedPath)))
                {
                    if (resolved.Equals(owned, comparison) || resolved.StartsWith(owned + Path.DirectorySeparatorChar, comparison))
                        throw new IOException("Native input resolution read a header or dependency from the formal BGCS output it would replace.");
                    if (resolved.Equals(cacheOwned, comparison) || resolved.StartsWith(cacheOwned + Path.DirectorySeparatorChar, comparison))
                        throw new IOException("Native or extension input resolution cannot read from the BGCS generation cache.");
                }
            }

            void NormalizeCandidatePaths(JsonNode? node)
            {
                if (node is JsonObject properties)
                    foreach (string name in properties.Select(property => property.Key).ToArray())
                    {
                        if (properties[name] is JsonValue scalar && scalar.TryGetValue(out string? text)) properties[name] = Logical(text);
                        else NormalizeCandidatePaths(properties[name]);
                        if (name == "excludedDirectories" && properties[name] is JsonArray excluded)
                            properties[name] = new JsonArray(excluded.Select(value => value!.GetValue<string>()).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                                .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                    }
                else if (node is JsonArray values)
                    for (int index = 0; index < values.Count; index++)
                    {
                        if (values[index] is JsonValue scalar && scalar.TryGetValue(out string? text)) values[index] = Logical(text);
                        else NormalizeCandidatePaths(values[index]);
                    }

                string? Logical(string? text)
                {
                    if (text is null) return null;
                    string candidate = text.Replace('\\', '/');
                    if (request.previewOutputRoot is not null && candidate.TrimEnd('/').Equals(normalizedPreviewOutput, publicationPathComparison))
                        return logicalOutput;
                    foreach (string normalized in normalizedCandidateOrigins)
                    {
                        if (candidate.Equals(normalized, publicationPathComparison)) return "artifact:/";
                        if (candidate.StartsWith(normalized + "/", publicationPathComparison)) return "artifact:/" + candidate[(normalized.Length + 1)..];
                    }
                    return text;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception failure)
        {
            diagnostics.Add(failure is BGCS.CSharp.Validation.CSharpPlanningException plannedFailure ? plannedFailure.diagnostic
                : new(BindingDiagnosticSeverity.Error, failure.Message, "BGCS-GENERATION-SESSION"));
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
        { targetModels = targetModels.ToImmutable(), nativeReferences = published ? nativeReferences.ToImmutable() : [], cacheStages = cacheEvidence.ToImmutableArray() };

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
                if (service is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
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
