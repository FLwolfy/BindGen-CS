namespace BGCS
{
    using BGCS.Application;
    using BGCS.Core;
    using BGCS.Core.Caching;
    using BGCS.Core.Logging;
    using BGCS.Configuration;
    using BGCS.CppAst.Diagnostics;
    using BGCS.CppAst.Model;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Parsing;
    using BGCS.CppAst.Targeting;
    using BGCS.Emission;
    using BGCS.Generation;
    using BGCS.Intermediate;
    using BGCS.Metadata;
    using BGCS.Output;
    using BGCS.Patching;
    using BGCS.Platform;
    using BGCS.PreProcessSteps;
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

        protected PatchEngine patchEngine = new();
        private CsCodeGeneratorMetadata metadata = new();
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
        /// Exposes public member <c>patchEngine</c>.
        /// </summary>
        public PatchEngine PatchEngine => patchEngine;

        /// <summary>
        /// Gets or sets the <c>PreProcessSteps</c> setting used by this component.
        /// </summary>
        public List<PreProcessStep> PreProcessSteps { get; } = new();

        /// <summary>
        /// Gets or sets the <c>CLIOptions</c> setting used by this component.
        /// </summary>
        public CLIGeneratorOptions? CLIOptions { get; set; }

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
            ConfigureGeneratorCore(PreProcessSteps);
            config.DefinedCppEnums = [];
            metadata = new();
            metadata.Settings = config;
            OnPostConfigure(config);
        }

        protected virtual void ConfigureGeneratorCore(List<PreProcessStep> preProcessSteps)
        {
            preProcessSteps.Add(new ConstantPreProcessStep(this, config));
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
            config.EnableIncrementalCache && GetType() == typeof(CsCodeGenerator) && config.HeaderInjector == null;

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
            new SingleFileComposer().Compose(files, mergedPath, config.Namespace,
                config.ResolvedTarget.Identifier);
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
            metadata = new() { Settings = config };
            LastResult = null;
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
        /// Performs the operation implemented by <c>GetMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetMetadata</c>.</returns>
        public CsCodeGeneratorMetadata GetMetadata()
        {
            return metadata;
        }
    }
}
