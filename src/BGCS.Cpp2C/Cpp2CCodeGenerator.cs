namespace BGCS.Cpp2C
{
    using BGCS.Core;
    using BGCS.Core.Caching;
    using BGCS.Core.IO;
    using BGCS.Cpp2C.Analysis;
    using BGCS.Cpp2C.Build;
    using BGCS.Cpp2C.Configuration;
    using BGCS.Cpp2C.Emission;
    using BGCS.Core.Logging;
    using BGCS.Cpp2C.GenerationSteps;
    using BGCS.Cpp2C.Metadata;
    using BGCS.Cpp2C.Adapters;
    using BGCS.CppAst.Diagnostics;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Parsing;
    using BGCS.CppAst.Targeting;
    using BGCS.Intermediate;
    using Newtonsoft.Json;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>Cpp2CCodeGenerator</c> used by the generation pipeline.
    /// </summary>
    public partial class Cpp2CCodeGenerator : BaseGenerator
    {
        private readonly Cpp2CGeneratorMetadata metadata = new();
        private readonly List<GenerationStep> generationSteps = [];
        private readonly List<Cpp2CGeneratorMetadata> copyFromPending = [];
        private bool hasCustomGenerationSteps;
        private bool pluginsApplied;

        /// <summary>
        /// Initializes a new instance of <see cref="Cpp2CCodeGenerator"/>.
        /// </summary>
        public Cpp2CCodeGenerator(Cpp2CGeneratorConfig settings) : base(settings)
        {
        }

        /// <summary>
        /// Gets the structured result of the most recent C++ bridge generation attempt.
        /// </summary>
        public BindingGenerationResult? LastResult { get; private set; }

        /// <summary>
        /// Exposes public member <c>generationSteps</c>.
        /// </summary>
        public IReadOnlyList<GenerationStep> GenerationSteps => generationSteps;

        /// <summary>
        /// Performs the operation implemented by <c>AddGenerationStep</c>.
        /// </summary>
        public void AddGenerationStep(GenerationStep step)
        {
            hasCustomGenerationSteps = true;
            generationSteps.Add(step);
        }

        /// <summary>
        /// Creates and registers a generation step of type <typeparamref name="T"/>.
        /// </summary>
        public void AddGenerationStep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : GenerationStep
        {
            hasCustomGenerationSteps = true;
            var step = (T)Activator.CreateInstance(typeof(T), this, config)!;
            generationSteps.Add(step);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetGenerationStep</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetGenerationStep</c>.</returns>
        public T GetGenerationStep<T>() where T : GenerationStep
        {
            foreach (var step in generationSteps)
            {
                if (step is T t)
                {
                    return t;
                }
            }

            throw new InvalidOperationException($"Generation step of type {typeof(T).Name} not found.");
        }

        /// <summary>
        /// Performs the operation implemented by <c>OverwriteGenerationStep</c>.
        /// </summary>
        public void OverwriteGenerationStep<TTarget>(GenerationStep newStep) where TTarget : GenerationStep
        {
            hasCustomGenerationSteps = true;
            for (int i = 0; i < generationSteps.Count; i++)
            {
                var step = generationSteps[i];
                if (step is TTarget)
                {
                    generationSteps[i] = newStep;
                }
            }
        }

        protected virtual CppParserOptions PrepareSettings()
        {
            Cpp2CConfigValidator.Validate(config);
            string baseDirectory = config.ConfigDirectory ?? Environment.CurrentDirectory;
            string? targetSysRoot = ResolveConfiguredPath(config.TargetSysRoot, baseDirectory, allowCommandName: false);
            string? compilerPath = ResolveConfiguredPath(config.CompilerPath, baseDirectory, allowCommandName: true);
            var options = new CppParserOptions
            {
                ParseMacros = config.ParseMacros,
                ParseComments = config.ParseComments,
                ParseSystemIncludes = config.ParseSystemIncludes,
                ParserKind = CppParserKind.Cpp,
                AutoSquashTypedef = true,
            };
            options.ConfigureForTarget(config.ResolvedTarget, targetSysRoot, compilerPath);

            for (int i = 0; i < config.AdditionalArguments.Count; i++)
            {
                options.AdditionalArguments.Add(config.AdditionalArguments[i]);
            }

            for (int i = 0; i < config.IncludeFolders.Count; i++)
            {
                options.IncludeFolders.Add(Path.GetFullPath(config.IncludeFolders[i], baseDirectory));
            }

            for (int i = 0; i < config.SystemIncludeFolders.Count; i++)
            {
                options.SystemIncludeFolders.Add(Path.GetFullPath(config.SystemIncludeFolders[i], baseDirectory));
            }

            for (int i = 0; i < config.Defines.Count; i++)
            {
                options.Defines.Add(config.Defines[i]);
            }

            //options.ConfigureForWindowsMsvc(CppTargetCpu.X86_64);
            if (!options.AdditionalArguments.Any(argument => argument.StartsWith("-std=", StringComparison.Ordinal)))
                options.AdditionalArguments.Add("-std=" + config.LanguageStandard);
            List<string> explicitInstantiations =
            [
                .. config.TemplateInstantiations.Select(type => $"template class {type};"),
                .. config.FunctionTemplateInstantiations.Select((declaration, index) => BuildFunctionTemplateForwarder(declaration, index))
            ];
            if (explicitInstantiations.Count > 0)
                options.PostHeaderText = string.Join(Environment.NewLine, explicitInstantiations);

            return options;
        }

        private static string? ResolveConfiguredPath(string? value, string baseDirectory, bool allowCommandName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (allowCommandName && !Path.IsPathRooted(value) && !value.Contains('/') && !value.Contains('\\'))
                return value;
            return Path.GetFullPath(value, baseDirectory);
        }

        private static string BuildFunctionTemplateForwarder(string declaration, int index)
        {
            int separator = declaration.IndexOf(' ');
            int open = declaration.LastIndexOf('(');
            int close = declaration.LastIndexOf(')');
            if (separator <= 0 || open <= separator || close <= open)
                throw new InvalidOperationException($"Invalid function-template instantiation declaration '{declaration}'. Expected 'ReturnType Namespace::Function<Args>(ParameterTypes)'.");
            string returnType = declaration[..separator].Trim();
            string function = declaration[(separator + 1)..open].Trim();
            string parametersText = declaration[(open + 1)..close].Trim();
            List<string> parameterTypes = SplitTemplateParameters(parametersText);
            string parameters = string.Join(", ", parameterTypes.Select((type, parameterIndex) => $"{type} arg{parameterIndex}"));
            string arguments = string.Join(", ", parameterTypes.Select((_, parameterIndex) => $"arg{parameterIndex}"));
            string invocation = $"{function}({arguments})";
            string statement = returnType == "void" ? invocation + ";" : "return " + invocation + ";";
            return $"inline {returnType} BGCS_FunctionTemplate_{index}({parameters}) {{ {statement} }}";
        }

        private static List<string> SplitTemplateParameters(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters) || parameters == "void")
                return [];
            List<string> result = [];
            int depth = 0;
            int start = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i] == '<') depth++;
                else if (parameters[i] == '>') depth--;
                else if (parameters[i] == ',' && depth == 0)
                {
                    result.Add(parameters[start..i].Trim());
                    start = i + 1;
                }
            }
            result.Add(parameters[start..].Trim());
            return result;
        }

        /// <summary>
        /// Generates the configured C++ bridge using paths relative to the loaded configuration file.
        /// </summary>
        /// <param name="outputPath">Optional output override, relative to the configuration directory unless absolute.</param>
        public void GenerateConfigured(string? outputPath = null)
        {
            if (config.EntryFiles.Count == 0)
                throw new InvalidOperationException("Cpp2C configuration requires at least one EntryFiles item.");
            string baseDirectory = config.ConfigDirectory ?? Environment.CurrentDirectory;
            List<string> headers = config.EntryFiles.Select(path => Path.GetFullPath(path, baseDirectory)).ToList();
            List<string>? allowedHeaders = config.AllowedHeaders.Count == 0
                ? null
                : config.AllowedHeaders.Select(path => Path.GetFullPath(path, baseDirectory)).ToList();
            string resolvedOutput = Path.GetFullPath(outputPath ?? config.OutputPath, baseDirectory);
            ApplyPluginServices();
            CppParserOptions options = PrepareSettings();
            IncrementalGenerationCache? cache = null;
            IncrementalCacheKey? cacheKey = null;
            if (CanUseIncrementalCache())
            {
                string cachePath = Path.GetFullPath(config.CacheDirectory, baseDirectory);
                cache = new(cachePath);
                IReadOnlyList<string> inputs = IncrementalGenerationCache.DiscoverInputs(headers,
                    options.IncludeFolders.Concat(options.SystemIncludeFolders), [resolvedOutput, cachePath]);
                string fingerprint = "cpp-bridge\n" + GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D") + "\n" +
                    JsonConvert.SerializeObject(config, Cpp2CGeneratorConfig.SerializerSettings) + "\n" +
                    string.Join("\n", options.AdditionalArguments.Concat(options.Defines)) + "\n" +
                    CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.Cpp,
                        ResolveConfiguredPath(config.CompilerPath, baseDirectory, allowCommandName: true)) + "\n" +
                    config.Plugins.GetCacheFingerprint() + "\n" +
                    (config.Adapters.TryGetCacheFingerprint(out string adapterFingerprint) ? adapterFingerprint : "adapters:unfingerprinted");
                cacheKey = IncrementalGenerationCache.CreateKey(fingerprint, inputs);
                if (cache.TryRestore(cacheKey, resolvedOutput, out string stateJson))
                {
                    CachedGenerationState state = JsonConvert.DeserializeObject<CachedGenerationState>(stateJson)
                        ?? throw new InvalidDataException("Incremental C++ bridge cache metadata is invalid.");
                    LastResult = new(state.Module, true, EnumerateOutputFiles(resolvedOutput), state.Diagnostics, true, cacheKey.Value);
                    LogInfo($"Restored generated C++ bridge from cache {cacheKey.Value[..12]}.");
                    return;
                }
            }
            Generate(options, headers, resolvedOutput, allowedHeaders);
            if (LastResult?.Success == true && cache != null && cacheKey != null)
            {
                cache.Store(cacheKey, resolvedOutput,
                    JsonConvert.SerializeObject(new CachedGenerationState(LastResult.Module, LastResult.Diagnostics.ToArray())));
                LastResult = new(LastResult.Module, true, LastResult.OutputFiles, LastResult.Diagnostics, false, cacheKey.Value);
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        public virtual void Generate(string headerFile, string outputPath, List<string>? allowedHeaders = null)
        {
            Generate([headerFile], outputPath, allowedHeaders);
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        public virtual void Generate(List<string> headerFiles, string outputPath, List<string>? allowedHeaders = null)
        {
            EnsureGenerationPipeline();
            var options = PrepareSettings();

            Generate(options, headerFiles, outputPath, allowedHeaders);
        }

        private void Generate(CppParserOptions options, List<string> headerFiles, string outputPath, List<string>? allowedHeaders)
        {
            EnsureGenerationPipeline();

            var compilation = CppParser.ParseFiles(headerFiles, options);

            Generate(compilation, headerFiles, outputPath, allowedHeaders);
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        public virtual void Generate(CppCompilation compilation, List<string> headerFiles, string outputPath, List<string>? allowedHeaders)
        {
            Cpp2CConfigValidator.Validate(config);
            EnsureGenerationPipeline();
            // Print diagnostic messages
            for (int i = 0; i < compilation.Diagnostics.Messages.Count; i++)
            {
                CppDiagnosticMessage? message = compilation.Diagnostics.Messages[i];
                if (message.Type == CppLogMessageType.Error && config.CppLogLevel <= LogSeverity.Error)
                {
                    LogError(message.ToString());
                }
                if (message.Type == CppLogMessageType.Warning && config.CppLogLevel <= LogSeverity.Warning)
                {
                    LogWarn(message.ToString());
                }
                if (message.Type == CppLogMessageType.Info && config.CppLogLevel <= LogSeverity.Information)
                {
                    LogInfo(message.ToString());
                }
            }

            if (compilation.HasErrors)
            {
                LastResult = new(null, false, [], CreateDiagnostics());
                return;
            }

            List<string> effectiveAllowedHeaders = allowedHeaders == null
                ? [.. headerFiles]
                : [.. allowedHeaders, .. headerFiles];
            if (config.TemplateInstantiations.Count > 0 || config.FunctionTemplateInstantiations.Count > 0)
                effectiveAllowedHeaders.Add(CppParser.CppAstRootFileName);
            FileSet files = new(effectiveAllowedHeaders.Select(PathHelper.GetPath));
            using OutputDirectoryTransaction outputTransaction = new(outputPath);
            string generationOutputPath = outputTransaction.StagingPath;
            Directory.CreateDirectory(Path.Combine(generationOutputPath, "include"));
            Directory.CreateDirectory(Path.Combine(generationOutputPath, "src"));

            foreach (var meta in copyFromPending)
            {
                foreach (var step in generationSteps)
                {
                    step.CopyFromMetadata(meta);
                }
            }

            try
            {
                ParseResult result = new(compilation, headerFiles);
                new CBridgeEmitter().EmitAst(this, generationSteps, files, result, generationOutputPath, config, metadata);
                if (config.GenerateBuildManifest)
                    CppBridgeBuildManifestEmitter.Emit(config, headerFiles, generationOutputPath);
                BindingModule module = new CppBridgeModuleAnalyzer(config).Analyze(compilation);
                outputTransaction.Commit();
                LastResult = new(module, true,
                    Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray(),
                    [.. CreateDiagnostics(), .. module.StructuredDiagnostics]);
            }
            catch (Exception exception)
            {
                string remediation = BuildUnsupportedRemediation(exception);
                LogError(remediation);
                LastResult = new(null, false, [],
                    [.. CreateDiagnostics(), new(BindingDiagnosticSeverity.Error, remediation, BindingDiagnosticCodes.CppUnsupported)]);
            }
        }

        private static string BuildUnsupportedRemediation(Exception exception)
        {
            return $"C++ bridge generation rejected an unsupported declaration: {exception.Message} " +
                "Add the required type name to the matching configurable adapter list (string, span/vector/array, map/set, optional/expected/variant, path, chrono, or smart pointer); " +
                "request a concrete class through TemplateInstantiations; or provide a custom generation step for ownership/allocator semantics.";
        }

        private BindingDiagnostic[] CreateDiagnostics()
        {
            return Messages.Select(message => new BindingDiagnostic(
                (BindingDiagnosticSeverity)(int)message.Severtiy, message.Message)).ToArray();
        }

        /// <summary>
        /// Performs the operation implemented by <c>Reset</c>.
        /// </summary>
        public virtual void Reset()
        {
            foreach (var step in GenerationSteps)
            {
                step.Reset();
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>CopyFrom</c>.
        /// </summary>
        public void CopyFrom(Cpp2CGeneratorMetadata metadata)
        {
            copyFromPending.Add(metadata);
        }

        /// <summary>
        /// Performs the operation implemented by <c>SaveMetadata</c>.
        /// </summary>
        public void SaveMetadata(string path)
        {
            JsonSerializerSettings options = new() { Formatting = Formatting.Indented };
            var json = JsonConvert.SerializeObject(metadata, options);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Performs the operation implemented by <c>LoadMetadata</c>.
        /// </summary>
        public void LoadMetadata(string path)
        {
            var json = File.ReadAllText(path);
            var metadata = JsonConvert.DeserializeObject<Cpp2CGeneratorMetadata>(json) ?? new();
            CopyFrom(metadata);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetMetadata</c>.</returns>
        public Cpp2CGeneratorMetadata GetMetadata()
        {
            return metadata;
        }

        private void EnsureGenerationPipeline()
        {
            if (generationSteps.Count != 0)
            {
                return;
            }

            EnsureDefaultGenerationSteps();
        }

        private void ApplyPluginServices()
        {
            if (pluginsApplied)
                return;
            foreach (var registration in config.Plugins.GetServices<ICppTypeAdapter>())
                if (!config.Adapters.TypeAdapters.Any(adapter => string.Equals(adapter.Name, registration.Service.Name, StringComparison.Ordinal)))
                    config.Adapters.Register(registration.Service);
            foreach (var registration in config.Plugins.GetServices<ICppCallableAdapter>())
                if (!config.Adapters.CallableAdapters.Any(adapter => string.Equals(adapter.Name, registration.Service.Name, StringComparison.Ordinal)))
                    config.Adapters.Register(registration.Service);
            pluginsApplied = true;
        }

        private bool CanUseIncrementalCache() => config.EnableIncrementalCache &&
            GetType() == typeof(Cpp2CCodeGenerator) && !hasCustomGenerationSteps && copyFromPending.Count == 0 &&
            config.Adapters.TryGetCacheFingerprint(out _);

        private static string[] EnumerateOutputFiles(string outputPath) =>
            Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        private sealed record CachedGenerationState(BindingModule? Module, BindingDiagnostic[] Diagnostics);

        private void EnsureDefaultGenerationSteps()
        {
            bool hasEnumStep = false;
            bool hasClassStep = false;

            for (int i = 0; i < generationSteps.Count; i++)
            {
                var step = generationSteps[i];
                if (step is EnumGenerationStep)
                {
                    hasEnumStep = true;
                }
                else if (step is ClassGenerationStep)
                {
                    hasClassStep = true;
                }
            }

            if (!hasEnumStep)
            {
                generationSteps.Add(new EnumGenerationStep(this, config));
            }

            if (!hasClassStep)
            {
                generationSteps.Add(new ClassGenerationStep(this, config));
            }
        }
    }
}
