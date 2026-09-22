namespace BGCS
{
    using BGCS.Application;
    using BGCS.Core;
    using BGCS.Core.Caching;
    using BGCS.Core.CSharp;
    using BGCS.Core.Logging;
    using BGCS.Configuration;
    using BGCS.CppAst.Diagnostics;
    using BGCS.CppAst.Model;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using BGCS.CppAst.Parsing;
    using BGCS.CppAst.Targeting;
    using BGCS.Emission;
    using BGCS.FunctionGeneration;
    using BGCS.Generation;
    using BGCS.GenerationSteps;
    using BGCS.Intermediate;
    using BGCS.Metadata;
    using BGCS.Output;
    using BGCS.Patching;
    using BGCS.Platform;
    using BGCS.PreProcessSteps;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsCodeGenerator</c> used by the generation pipeline.
    /// </summary>
    public partial class CsCodeGenerator : BaseGenerator
    {
        private const string RuntimeUsingDefault = "using BGCS.Runtime;";
        private static readonly Regex EmptyPartialTypeRegex = new(
            @"^\s*(?:(?:\s*///.*\r?\n)+)?(?:\s*\[[^\]\r\n]+\]\r?\n)*\s*public\s+(?:static\s+)?(?:readonly\s+)?(?:unsafe\s+)?partial\s+(?:class|struct)\s+\w+\s*\r?\n\s*\{\s*\r?\n\s*\}\s*(?:\r?\n)?",
            RegexOptions.Multiline | RegexOptions.Compiled);

        protected FunctionGenerator funcGen = null!;
        protected PatchEngine patchEngine = new();
        private CsCodeGeneratorMetadata metadata = new();
        /// <summary>
        /// Performs the operation implemented by <c>new</c>.
        /// </summary>
        /// <returns>Result produced by <c>new</c>.</returns>
        public readonly FunctionTableBuilder FunctionTableBuilder = new();
        private readonly List<GenerationStep> generationSteps = new();
        private Dictionary<string, string> wrappedPointers = null!;
        private List<CsCodeGeneratorMetadata> copyFromPending = [];
        private readonly BindingGenerationPipeline pipeline;

        /// <summary>
        /// Performs the operation implemented by <c>Create</c>.
        /// </summary>
        /// <returns>Result produced by <c>Create</c>.</returns>
        public static CsCodeGenerator Create(string configPath)
        {
            return new(new ConfigLoader().Load(configPath));
        }

        /// <summary>
        /// Performs the operation implemented by <c>CsCodeGenerator</c>.
        /// </summary>
        /// <returns>Result produced by <c>CsCodeGenerator</c>.</returns>
        public CsCodeGenerator(CsCodeGeneratorConfig config) : base(config)
        {
            if (!config.PresetDefaultsApplied)
            {
                PresetResolver.Default.Apply(config);
                config.PresetDefaultsApplied = true;
            }
            pipeline = new(config);
        }

        /// <summary>
        /// Gets the structured result of the most recent completed generation attempt.
        /// </summary>
        public BindingGenerationResult? LastResult { get; private set; }

        /// <summary>
        /// Exposes public member <c>}</c>.
        /// </summary>
        public FunctionGenerator FunctionGenerator { get => funcGen; protected set => funcGen = value; }

        /// <summary>
        /// Exposes public member <c>patchEngine</c>.
        /// </summary>
        public PatchEngine PatchEngine => patchEngine;

        /// <summary>
        /// Exposes public member <c>generationSteps</c>.
        /// </summary>
        public IReadOnlyList<GenerationStep> GenerationSteps => generationSteps;

        /// <summary>
        /// Gets or sets the <c>PreProcessSteps</c> setting used by this component.
        /// </summary>
        public List<PreProcessStep> PreProcessSteps { get; } = new();

        /// <summary>
        /// Gets or sets the <c>CLIOptions</c> setting used by this component.
        /// </summary>
        public CLIGeneratorOptions? CLIOptions { get; set; }

        /// <summary>
        /// Performs the operation implemented by <c>GetGenerationStep</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetGenerationStep</c>.</returns>
        public T GetGenerationStep<T>() where T : GenerationStep
        {
            foreach (var step in GenerationSteps)
            {
                if (step is T t)
                {
                    return t;
                }
            }

            throw new InvalidOperationException($"Step of type '{typeof(T)}' was not found.");
        }

        /// <summary>
        /// Performs the operation implemented by <c>AddGenerationStep</c>.
        /// </summary>
        public void AddGenerationStep(GenerationStep step)
        {
            generationSteps.Add(step);
        }

        /// <summary>
        /// Creates and registers a generation step of type <typeparamref name="T"/>.
        /// </summary>
        public void AddGenerationStep<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : GenerationStep
        {
            var step = (GenerationStep)Activator.CreateInstance(typeof(T), this, config)!;
            generationSteps.Add(step);
        }

        /// <summary>
        /// Performs the operation implemented by <c>OverwriteGenerationStep</c>.
        /// </summary>
        public void OverwriteGenerationStep<TTarget>(GenerationStep newStep) where TTarget : GenerationStep
        {
            for (int i = 0; i < GenerationSteps.Count; i++)
            {
                GenerationStep step = GenerationSteps[i];
                if (step is TTarget)
                {
                    generationSteps[i] = newStep;
                }
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public bool Generate(string headerFile, string outputPath, List<string>? allowedHeaders = null)
        {
            return Generate([headerFile], outputPath, allowedHeaders);
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public bool Generate(List<string> headerFiles, string outputPath, List<string>? allowedHeaders = null)
        {
            return Generate(PrepareSettings(), headerFiles, outputPath, allowedHeaders);
        }

        /// <summary>
        /// Generates all configured entry files using paths relative to the loaded configuration file.
        /// </summary>
        /// <param name="outputPath">Optional output path override. Relative paths use the configuration directory.</param>
        /// <returns><see langword="true"/> when parsing and generation complete without fatal diagnostics; otherwise <see langword="false"/>.</returns>
        public bool GenerateConfigured(string? outputPath = null)
        {
            ConfiguredGenerationRequest request = ConfiguredGenerationRequestResolver.Resolve(config, outputPath);
            CppParserOptions options = PrepareSettings();
            IncrementalCacheKey? cacheKey = null;
            IncrementalGenerationCache? cache = null;
            if (CanUseIncrementalCache())
            {
                string cachePath = Path.GetFullPath(config.CacheDirectory, request.BaseDirectory);
                cache = new(cachePath);
                cacheKey = CreateCacheKey(request, options, cachePath);
                if (cache.TryRestore(cacheKey, request.OutputPath, out string stateJson))
                {
                    CachedGenerationState state = JsonConvert.DeserializeObject<CachedGenerationState>(stateJson)
                        ?? throw new InvalidDataException("Incremental C# binding cache metadata is invalid.");
                    LastResult = new(state.Module, true, EnumerateOutputFiles(request.OutputPath), state.Diagnostics, true, cacheKey.Value);
                    LogInfo($"Restored generated bindings from cache {cacheKey.Value[..12]}.");
                    return true;
                }
            }
            bool success = Generate(options, request.HeaderFiles.ToList(), request.OutputPath, request.AllowedHeaders?.ToList());
            if (success && cache != null && cacheKey != null && LastResult != null)
            {
                cache.Store(cacheKey, request.OutputPath,
                    JsonConvert.SerializeObject(new CachedGenerationState(LastResult.Module, LastResult.Diagnostics.ToArray())));
                LastResult = new(LastResult.Module, true, LastResult.OutputFiles, LastResult.Diagnostics, false, cacheKey.Value);
            }
            return success;
        }

        /// <summary>
        /// Parses and analyzes configured headers without emitting or replacing generated output.
        /// </summary>
        /// <returns>A structured result containing the shared binding IR and parser diagnostics.</returns>
        public BindingGenerationResult AnalyzeConfigured()
        {
            ConfiguredGenerationRequest request = ConfiguredGenerationRequestResolver.Resolve(config, null);
            ConfigValidator.Validate(config);
            LogInfo($"Analyzing: {config.ApiName}");
            CppParserOptions options = PrepareSettings();
            CppCompilation compilation = ParseFiles(options, request.HeaderFiles.ToList());
                LogCompilationDiagnostics(compilation);
                List<string> allowedHeaders = request.AllowedHeaders?.ToList() ??
                    (config.IncludeTransitivelyReferencedHeaders
                        ? ResolveTransitiveUserHeaders(compilation, request.HeaderFiles, options.IncludeFolders)
                        : request.HeaderFiles.ToList());
                if (compilation.HasErrors)
                {
                    LastResult = pipeline.CreateResult(compilation, allowedHeaders, false, request.OutputPath, Messages);
                    return LastResult;
                }
                BindingModule module = pipeline.Analyze(compilation, allowedHeaders);
                bool safetySuccess = !module.StructuredDiagnostics.Any(diagnostic => diagnostic.Severity == BindingDiagnosticSeverity.Error);
                LastResult = new(module, safetySuccess, [],
                [
                    .. Messages.Select(diagnostic => new BindingDiagnostic(
                        (BindingDiagnosticSeverity)(int)diagnostic.Severtiy, diagnostic.Message)),
                    .. module.StructuredDiagnostics
                ]);
                return LastResult;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public bool Generate(CppParserOptions parserOptions, string headerFile, string outputPath, List<string>? allowedHeaders = null)
        {
            return Generate(parserOptions, [headerFile], outputPath, allowedHeaders);
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public bool Generate(CppParserOptions parserOptions, List<string> headerFiles, string outputPath, List<string>? allowedHeaders = null)
        {
            ArgumentNullException.ThrowIfNull(parserOptions);
            ArgumentNullException.ThrowIfNull(headerFiles);
            if (headerFiles.Count == 0)
            {
                throw new ArgumentException("At least one header file is required.", nameof(headerFiles));
            }
            ConfigureCore();
            LogInfo($"Generating: {config.ApiName}");

            LogInfo("Parsing Headers...");
            var compilation = ParseFiles(parserOptions, headerFiles);

            return GenerateCore(compilation, headerFiles, outputPath, allowedHeaders);
        }

        protected virtual void ConfigureCore()
        {
            PreProcessSteps.Clear();
            generationSteps.Clear();
            ConfigureGeneratorCore(PreProcessSteps, generationSteps, out funcGen);
            config.DefinedCppEnums = GetGenerationStep<EnumGenerationStep>().DefinedCppEnums;
            wrappedPointers = GetGenerationStep<TypeGenerationStep>().WrappedPointers;
            metadata = new();
            metadata.Settings = Settings;
            OnPostConfigure(config);
        }

        protected virtual void ConfigureGeneratorCore(List<PreProcessStep> preProcessSteps, List<GenerationStep> generationSteps, out FunctionGenerator funcGen)
        {
            funcGen = FunctionGenerator.CreateDefault(config);
            preProcessSteps.Add(new ConstantPreProcessStep(this, config));
            generationSteps.Add(new EnumGenerationStep(this, config));
            generationSteps.Add(new ConstantGenerationStep(this, config));
            generationSteps.Add(new HandleGenerationStep(this, config));
            generationSteps.Add(new TypedefAliasGenerationStep(this, config));
            generationSteps.Add(new TypeGenerationStep(this, config));
            generationSteps.Add(new FunctionGenerationStep(this, config));
            generationSteps.Add(new ExtensionGenerationStep(this, config));
            generationSteps.Add(new DelegateGenerationStep(this, config));
            OnConfigureGenerator();
        }

        protected virtual void OnConfigureGenerator()
        {
        }

        protected virtual CppParserOptions PrepareSettings()
        {
            string baseDirectory = config.ConfigDirectory ?? Environment.CurrentDirectory;
            string? targetSysRoot = ResolveConfiguredPath(config.TargetSysRoot, baseDirectory, allowCommandName: false);
            string? compilerPath = ResolveConfiguredPath(config.CompilerPath, baseDirectory, allowCommandName: true);
            var options = new CppParserOptions
            {
                ParseMacros = config.ParseMacros,
                ParseComments = config.ParseComments,
                ParseSystemIncludes = config.ParseSystemIncludes,

                ParseCommentAttribute = config.ParseComments,
                //ParseTokenAttributes = true,
                ParserKind = config.ParserKind,

                AutoSquashTypedef = config.AutoSquashTypedef,
            };
            options.ConfigureForTarget(config.ResolvedTarget, targetSysRoot, compilerPath);

            var additionalArguments = config.AdditionalArguments ?? [];
            var includeFolders = config.IncludeFolders ?? [];
            var systemIncludeFolders = config.SystemIncludeFolders ?? [];
            var defines = config.Defines ?? [];

            for (int i = 0; i < additionalArguments.Count; i++)
            {
                options.AdditionalArguments.Add(additionalArguments[i]);
            }

            for (int i = 0; i < includeFolders.Count; i++)
            {
                options.IncludeFolders.Add(Path.GetFullPath(includeFolders[i], baseDirectory));
            }

            for (int i = 0; i < systemIncludeFolders.Count; i++)
            {
                options.SystemIncludeFolders.Add(Path.GetFullPath(systemIncludeFolders[i], baseDirectory));
            }

            for (int i = 0; i < defines.Count; i++)
            {
                options.Defines.Add(defines[i]);
            }

            // options.ConfigureForWindowsMsvc(CppTargetCpu.X86_64);
            //options.AdditionalArguments.Add("-std=c++17");

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

        private bool CanUseIncrementalCache() =>
            config.EnableIncrementalCache && GetType() == typeof(CsCodeGenerator) && config.HeaderInjector == null &&
            copyFromPending.Count == 0;

        private IncrementalCacheKey CreateCacheKey(ConfiguredGenerationRequest request, CppParserOptions options, string cachePath)
        {
            IReadOnlyList<string> inputs = IncrementalGenerationCache.DiscoverInputs(
                request.HeaderFiles,
                options.IncludeFolders.Concat(options.SystemIncludeFolders),
                [request.OutputPath, cachePath]);
            string assemblyIdentity = GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D");
            string optionsFingerprint = string.Join("\n", options.AdditionalArguments.Concat(options.Defines));
            string fingerprint = "csharp\n" + assemblyIdentity + "\n" +
                JsonConvert.SerializeObject(config, CsCodeGeneratorConfig.SerializerSettings) + "\n" + optionsFingerprint + "\n" +
                CppToolchainDiscovery.GetCompilerFingerprint(config.ParserKind,
                    ResolveConfiguredPath(config.CompilerPath, request.BaseDirectory, allowCommandName: true)) + "\n" +
                config.Plugins.GetCacheFingerprint();
            return IncrementalGenerationCache.CreateKey(fingerprint, inputs);
        }

        private static string[] EnumerateOutputFiles(string outputPath) =>
            Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        private sealed record CachedGenerationState(BindingModule? Module, BindingDiagnostic[] Diagnostics);

        protected virtual CppCompilation ParseFiles(CppParserOptions parserOptions, List<string> headerFiles)
        {
            return CppParser.ParseFiles(headerFiles, parserOptions);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GenerateCore</c>.
        /// </summary>
        /// <returns>Result produced by <c>GenerateCore</c>.</returns>
        public virtual bool GenerateCore(CppCompilation compilation, List<string> headerFiles, string outputPath, List<string>? allowedHeaders = null)
        {
            return pipeline.Generate(this, compilation, headerFiles, outputPath, allowedHeaders);
        }

        internal CsCodeGeneratorConfig PipelineConfig => config;

        internal CsCodeGeneratorMetadata PipelineMetadata => metadata;

        internal IReadOnlyList<CsCodeGeneratorMetadata> PendingMetadata => copyFromPending;

        internal void SetLastResult(BindingGenerationResult result) => LastResult = result;

        internal void ReportCompilationDiagnostics(CppCompilation compilation) => LogCompilationDiagnostics(compilation);

        internal List<string> ResolveAllowedHeaders(CppCompilation compilation, IReadOnlyList<string> headerFiles)
        {
            string baseDirectory = config.ConfigDirectory ?? Environment.CurrentDirectory;
            string[] includeFolders = config.IncludeFolders.Select(path => Path.GetFullPath(path, baseDirectory)).ToArray();
            return config.IncludeTransitivelyReferencedHeaders
                ? ResolveTransitiveUserHeaders(compilation, headerFiles, includeFolders)
                : [.. headerFiles];
        }

        internal void InvokePrePatch(ParseResult result, List<string> headerFiles) => OnPrePatchCore(result, headerFiles);

        internal void RewriteGeneratedRuntimeUsings(string outputPath) => RewriteRuntimeUsings(outputPath);

        internal static void RemoveEmptyGeneratedTypes(string outputPath) => RemoveEmptyPartialTypes(outputPath);

        internal static void RemoveEmptyGeneratedDirectories(string outputPath) => DeleteEmptyDirectories(outputPath);

        internal void ComposeSingleFile(string generationOutputPath) => MergeGeneratedFilesToSingleFile(generationOutputPath, generationOutputPath);

        internal void EmitStandaloneRuntime(string outputPath) => WriteStandaloneRuntimeFile(outputPath);

        private void LogCompilationDiagnostics(CppCompilation compilation)
        {
            for (int i = 0; i < compilation.Diagnostics.Messages.Count; i++)
            {
                CppDiagnosticMessage message = compilation.Diagnostics.Messages[i];
                if (message.Type == CppLogMessageType.Error && config.CppLogLevel <= LogSeverity.Error)
                    LogError(message.ToString());
                if (message.Type == CppLogMessageType.Warning && config.CppLogLevel <= LogSeverity.Warning)
                    LogWarn(message.ToString());
                if (message.Type == CppLogMessageType.Info && config.CppLogLevel <= LogSeverity.Information)
                    LogInfo(message.ToString());
            }
        }

        private static List<string> ResolveTransitiveUserHeaders(CppCompilation compilation, IReadOnlyList<string> headerFiles, IReadOnlyList<string> includeFolders)
        {
            HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
            foreach (string headerFile in headerFiles)
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(headerFile));
                if (!string.IsNullOrEmpty(directory))
                {
                    roots.Add(directory);
                }
            }
            foreach (string includeFolder in includeFolders)
            {
                if (!string.IsNullOrWhiteSpace(includeFolder))
                {
                    roots.Add(Path.GetFullPath(includeFolder));
                }
            }

            HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
            HashSet<ICppContainer> visited = new(ReferenceEqualityComparer.Instance);
            foreach (string headerFile in headerFiles)
            {
                sources.Add(Path.GetFullPath(headerFile));
            }
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string candidate in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(candidate);
                    if (extension is ".h" or ".hh" or ".hpp" or ".hxx" or ".inc")
                        sources.Add(Path.GetFullPath(candidate));
                }
            }
            foreach (CppMacro macro in compilation.Macros)
            {
                AddSource(macro.SourceFile);
            }
            Collect(compilation);
            return [.. sources];

            void Collect(ICppContainer container)
            {
                if (!visited.Add(container))
                {
                    return;
                }
                foreach (ICppDeclaration declaration in container.Children)
                {
                    if (declaration is CppElement element)
                    {
                        AddSource(element.SourceFile);
                    }
                    if (declaration is ICppContainer nested)
                    {
                        Collect(nested);
                    }
                }
            }

            void AddSource(string? sourceFile)
            {
                if (string.IsNullOrWhiteSpace(sourceFile))
                {
                    return;
                }
                string fullPath = Path.GetFullPath(sourceFile);
                foreach (string root in roots)
                {
                    if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(fullPath);
                        return;
                    }
                }
            }
        }

        private static void RemoveEmptyPartialTypes(string generationOutputPath)
        {
            string[] files = Directory.GetFiles(generationOutputPath, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string text = File.ReadAllText(path);
                string updated = EmptyPartialTypeRegex.Replace(text, string.Empty);
                if (!ReferenceEquals(text, updated) && text != updated)
                {
                    File.WriteAllText(path, updated);
                }

                if (IsScaffoldingOnlyCSharpFile(updated))
                {
                    File.Delete(path);
                }
            }
        }

        private static bool IsScaffoldingOnlyCSharpFile(string text)
        {
            string normalized = text.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line == "{" || line == "}")
                {
                    continue;
                }

                if (line.StartsWith("namespace ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("using ", StringComparison.Ordinal) &&
                    line.EndsWith(';') &&
                    !line.Contains('='))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        protected virtual void MergeGeneratedFilesToSingleFile(string generationOutputPath, string outputPath)
        {
            string mergedPath = Path.Combine(outputPath, SingleFileOutputNameResolver.Resolve(config));
            List<string> files = Directory.GetFiles(generationOutputPath, "*.cs", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, mergedPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
                return;
            new SingleFileComposer().Compose(files, mergedPath, config.Namespace);
            foreach (string file in files)
                File.Delete(file);
            DeleteEmptyDirectories(generationOutputPath);
            LogInfo($"Merged generated files into: {mergedPath}");
        }

        private void RewriteRuntimeUsings(string generationOutputPath)
        {
            string[] files = Directory.GetFiles(generationOutputPath, "*.cs", SearchOption.AllDirectories);
            string runtimeUsingTarget = $"using {GetRuntimeNamespace()};";

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string text = File.ReadAllText(path);
                if (!text.Contains(RuntimeUsingDefault, StringComparison.Ordinal))
                {
                    continue;
                }

                text = text.Replace(RuntimeUsingDefault, runtimeUsingTarget, StringComparison.Ordinal);
                text = text.Replace("Utils.", $"global::{GetRuntimeNamespace()}.Utils.", StringComparison.Ordinal);

                File.WriteAllText(path, text);
            }
        }

        private void WriteStandaloneRuntimeFile(string outputPath)
        {
            BindingModule module = new(config.ApiName, config.Namespace, config.LibName,
                config.ResolvedTarget.Identifier);
            string runtimeNamespace = GetRuntimeNamespace();
            string runtimePath = new RuntimeEmitter().Emit(module,
                new(outputPath, true, "Runtime.cs", runtimeNamespace)).Single();
            LogInfo($"Generated runtime file: {runtimePath}");
        }

        private string GetRuntimeNamespace()
        {
            return string.IsNullOrWhiteSpace(config.RuntimeNamespace)
                ? "BGCS.Runtime"
                : config.RuntimeNamespace;
        }

        private static void DeleteEmptyDirectories(string rootPath)
        {
            string[] dirs = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
            Array.Sort(dirs, (a, b) => b.Length.CompareTo(a.Length));

            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                bool hasFiles = Directory.EnumerateFiles(dir).Any();
                bool hasSubDirs = Directory.EnumerateDirectories(dir).Any();
                if (!hasFiles && !hasSubDirs)
                {
                    Directory.Delete(dir, false);
                }
            }
        }

        protected virtual void OnPrePatchCore(ParseResult result, List<string> headerFiles)
        {
            LogInfo("Applying Pre-Patches...");
            patchEngine.ApplyPrePatches(config, AppDomain.CurrentDomain.BaseDirectory, headerFiles, result);
            OnPrePatch(result, headerFiles);
        }

        protected virtual void OnPrePatch(ParseResult result, List<string> headerFiles)
        {
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
        public void CopyFrom(CsCodeGeneratorMetadata metadata)
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
            var metadata = JsonConvert.DeserializeObject<CsCodeGeneratorMetadata>(json) ?? new();
            CopyFrom(metadata);
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetMetadata</c>.</returns>
        public CsCodeGeneratorMetadata GetMetadata()
        {
            return metadata;
        }

        /// <summary>
        /// Performs the operation implemented by <c>FindFunction</c>.
        /// </summary>
        /// <returns>Result produced by <c>FindFunction</c>.</returns>
        public static CppFunction FindFunction(CppCompilation compilation, string name)
        {
            for (int i = 0; i < compilation.Functions.Count; i++)
            {
                var function = compilation.Functions[i];
                if (function.Name == name)
                    return function;
            }

            throw new Exception($"Function '{name}' not found!");
        }

        /// <summary>
        /// Performs the operation implemented by <c>PrepareArgs</c>.
        /// </summary>
        public void PrepareArgs(CsFunctionVariation variation, CsType csReturnType)
        {
            if (wrappedPointers.TryGetValue(csReturnType.Name, out string? value))
            {
                csReturnType.Name = value;
            }

            for (int i = 0; i < variation.Parameters.Count; i++)
            {
                var cppParameter = variation.Parameters[i];
                if (wrappedPointers.TryGetValue(cppParameter.Type.Name, out string? v))
                {
                    cppParameter.Type.Name = v;
                    cppParameter.Type.Classify();
                }
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>CreateCsFunction</c>.
        /// </summary>
        /// <returns>Result produced by <c>CreateCsFunction</c>.</returns>
        public virtual CsFunction CreateCsFunction(CppFunction cppFunction, CsFunctionKind kind, string csName, List<CsFunction> functions, out CsFunctionOverload overload)
        {
            config.TryGetFunctionMapping(cppFunction.Name, out var mapping);

            string returnCsName = config.GetCsReturnType(cppFunction.ReturnType);
            CppPrimitiveKind returnKind = cppFunction.ReturnType.GetPrimitiveKind();

            CsFunction? function = null;
            for (int j = 0; j < functions.Count; j++)
            {
                if (functions[j].Name == csName)
                {
                    function = functions[j];
                    break;
                }
            }

            if (function == null)
            {
                config.WriteCsSummary(cppFunction.Comment, out string? comment);
                if (mapping != null && mapping.Comment != null)
                {
                    comment = config.WriteCsSummary(mapping.Comment);
                }
                function = new(csName, comment);
                functions.Add(function);
            }

            overload = new(cppFunction.Name, csName, function.Comment, "", kind, new(returnCsName, returnKind));
            if (config.GenerateMetadata)
            {
                overload.Attributes.Add($"[NativeName(NativeNameType.Func, \"{cppFunction.Name}\")]");
                overload.Attributes.Add($"[return: NativeName(NativeNameType.Type, \"{cppFunction.ReturnType.GetDisplayName()}\")]");
            }
            for (int j = 0; j < cppFunction.Parameters.Count; j++)
            {
                var cppParameter = cppFunction.Parameters[j];
                var paramCsTypeName = config.GetCsTypeName(cppParameter.Type);
                var paramCsName = config.GetParameterName(j, cppParameter.Name);
                var direction = cppParameter.Type.GetDirection();
                var primKind = cppParameter.Type.GetPrimitiveKind();

                CsType csType = new(paramCsTypeName, primKind);

                CsParameterInfo csParameter = new(paramCsName, cppParameter.Type, csType, direction);
                if (config.GenerateMetadata)
                {
                    csParameter.Attributes.Add($"[NativeName(NativeNameType.Param, \"{cppParameter.Name}\")]");
                    csParameter.Attributes.Add($"[NativeName(NativeNameType.Type, \"{cppParameter.Type.GetDisplayName()}\")]");
                }
                overload.Parameters.Add(csParameter);
                if (config.TryGetDefaultValue(cppFunction.Name, cppParameter, false, out var defaultValue))
                {
                    overload.DefaultValues.Add(paramCsName, defaultValue!);
                }
            }

            function.Overloads.Add(overload);
            return function;
        }

        /// <summary>
        /// Performs the operation implemented by <c>CreateCsDelegate</c>.
        /// </summary>
        /// <returns>Result produced by <c>CreateCsDelegate</c>.</returns>
        public virtual CsDelegate CreateCsDelegate<T>(T member, string csName, CppFunctionType functionType) where T : class, ICppDeclaration, ICppMember
        {
            config.WriteCsSummary(member.Comment, out string? comment);

            string returnCsName = config.GetCsReturnType(functionType.ReturnType);
            CppPrimitiveKind returnKind = functionType.ReturnType.GetPrimitiveKind();

            List<CsParameterInfo> parameters = [];

            for (int j = 0; j < functionType.Parameters.Count; j++)
            {
                var cppParameter = functionType.Parameters[j];
                var paramCsTypeName = config.GetCsTypeName(cppParameter.Type);
                var paramCsName = config.GetParameterName(j, cppParameter.Name);
                var direction = cppParameter.Type.GetDirection();
                var primKind = cppParameter.Type.GetPrimitiveKind();

                CsType csType = new(paramCsTypeName, primKind);

                CsParameterInfo csParameter = new(paramCsName, cppParameter.Type, csType, direction);
                if (config.GenerateMetadata)
                {
                    csParameter.Attributes.Add($"[NativeName(NativeNameType.Param, \"{cppParameter.Name}\")]");
                    csParameter.Attributes.Add($"[NativeName(NativeNameType.Type, \"{cppParameter.Type.GetDisplayName()}\")]");
                }
                parameters.Add(csParameter);
            }

            List<string> attributes = [];

            if (config.GenerateMetadata)
            {
                attributes.Add($"[NativeName(NativeNameType.Delegate, \"{member.Name}\")]");
                attributes.Add($"[return: NativeName(NativeNameType.Type, \"{functionType.ReturnType.GetDisplayName()}\")]");
            }
            attributes.Add($"[UnmanagedFunctionPointer(CallingConvention.{functionType.CallingConvention.GetCallingConvention()})]");

            return new(member.Name, csName, new(returnCsName, returnKind), parameters, attributes, comment);
        }

        protected virtual string BuildFunctionSignature(CsFunctionVariation variation, bool useAttributes, bool useNames, WriteFunctionFlags flags)
        {
            int offset = flags == WriteFunctionFlags.None ? 0 : 1;
            StringBuilder sb = new();
            bool isFirst = true;

            if (flags == WriteFunctionFlags.Extension)
            {
                isFirst = false;
                var first = variation.Parameters[0];
                if (useNames)
                {
                    sb.Append($"this {first.Type} {first.Name}");
                }
                else
                {
                    sb.Append($"this {first.Type}");
                }
            }

            for (int i = offset; i < variation.Parameters.Count; i++)
            {
                var param = variation.Parameters[i];

                if (param.DefaultValue != null)
                    continue;

                if (!isFirst)
                    sb.Append(", ");

                if (useAttributes)
                {
                    sb.Append($"{string.Join(" ", param.Attributes)} ");
                }

                sb.Append($"{param.Type}");

                if (useNames)
                {
                    sb.Append($" {param.Name}");
                }

                isFirst = false;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Performs the operation implemented by <c>BuildFunctionHeaderId</c>.
        /// </summary>
        /// <returns>Result produced by <c>BuildFunctionHeaderId</c>.</returns>
        public virtual string BuildFunctionHeaderId(CsFunctionVariation variation, WriteFunctionFlags flags)
        {
            string signature = BuildFunctionSignature(variation, false, false, flags);
            return $"{variation.Name}({signature})";
        }

        /// <summary>
        /// Performs the operation implemented by <c>BuildFunctionHeader</c>.
        /// </summary>
        /// <returns>Result produced by <c>BuildFunctionHeader</c>.</returns>
        public virtual string BuildFunctionHeader(CsFunctionVariation variation, CsType csReturnType, WriteFunctionFlags flags, bool generateMetadata)
        {
            string signature = BuildFunctionSignature(variation, generateMetadata, true, flags);
            return $"{csReturnType.Name} {variation.Name}({signature})";
        }

        /// <summary>
        /// Performs the operation implemented by <c>ClassifyParameters</c>.
        /// </summary>
        public static void ClassifyParameters(CsFunctionOverload overload, CsFunctionVariation variation, CsType csReturnType, out bool firstParamReturn, out int offset, out bool hasManaged)
        {
            firstParamReturn = false;
            if (!csReturnType.IsString && csReturnType.Name != overload.ReturnType.Name)
            {
                firstParamReturn = true;
            }

            offset = firstParamReturn ? 1 : 0;
            hasManaged = false;
            for (int j = 0; j < variation.Parameters.Count - offset; j++)
            {
                var cppParameter = variation.Parameters[j + offset];

                if (cppParameter.DefaultValue == null)
                {
                    continue;
                }

                var paramCsDefault = cppParameter.DefaultValue;
                if (cppParameter.Type.IsString || paramCsDefault.StartsWith("\"") && paramCsDefault.EndsWith("\""))
                {
                    hasManaged = true;
                }
            }
        }
    }
}
