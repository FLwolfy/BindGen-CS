using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Analysis;
using BGCS.Core.IO;

namespace BGCS.Facade
{
    using System.Text.RegularExpressions;
    using BGCS.Analysis.PreProcessing;
    using BGCS.Application;
    using BGCS.Configuration;
    using BGCS.Core.Caching;
    using BGCS.Core.Logging;
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
    using Newtonsoft.Json;

    /// <summary>
    /// Owns one configurable C-to-C# generation service, using shared analysis and frozen-IR emission through an atomic output transaction.
    /// </summary>
    public partial class CsCodeGenerator : BaseGenerator
    {
        private const string C_RUNTIMEUSINGDEFAULT = "using BGCS.Runtime;";
        private static readonly Regex EmptyPartialTypeRegex = new(@"^\s*(?:(?:\s*///.*\r?\n)+)?(?:\s*\[[^\]\r\n]+\]\r?\n)*\s*public\s+(?:static\s+)?(?:readonly\s+)?(?:unsafe\s+)?partial\s+(?:class|struct)\s+\w+\s*\r?\n\s*\{\s*\r?\n\s*\}\s*(?:\r?\n)?", RegexOptions.Multiline | RegexOptions.Compiled);
        private readonly PatchEngine m_patchEngine = new();
        private CsCodeGeneratorMetadata m_metadata = new();
        private readonly BindingGenerationPipeline m_pipeline;
        /// <summary>
        /// Loads and validates a configuration document and creates a generation service using its resolved plugins and target.
        /// </summary>
        /// <param name="configPath">
        /// The configuration file resolved relative to the current directory.
        /// </param>
        /// <returns>
        /// A newly owned generator retaining the loaded mutable configuration.
        /// </returns>
        public static CsCodeGenerator Create(string configPath)
        {
            return new(new ConfigLoader().Load(configPath));
        }

        /// <summary>
        /// Retains the caller's configuration and applies preset defaults once before preparing the shared generation pipeline.
        /// </summary>
        /// <param name="config">
        /// The mutable configuration retained by this generator; its plugin lifetime belongs to the caller.
        /// </param>
        public CsCodeGenerator(CsCodeGeneratorConfig config) : base(config)
        {
            if (!config.presetDefaultsApplied)
            {
                PresetResolver.@default.Apply(config);
                config.presetDefaultsApplied = true;
            }

            this.m_pipeline = new(config);
        }

        /// <summary>
        /// Gets the structured result of the most recent completed generation attempt.
        /// </summary>
        public BindingGenerationResult<BindingModule>? lastResult { get; private set; }
        /// <summary>
        /// Gets the retained pre- and post-generation patch registry; registered patches participate in later attempts.
        /// </summary>
        public PatchEngine patchEngine => m_patchEngine;
        /// <summary>
        /// Gets the attempt-local preprocessing sequence; configuration clears and repopulates it before each attempt.
        /// </summary>
        public List<PreProcessStep> preProcessSteps { get; } = new();
        /// <summary>
        /// Notifies callers after per-attempt configuration is ready and before preprocessing begins.
        /// </summary>
        public event Action<CsCodeGenerator, CsCodeGeneratorConfig>? PostConfigure;

        /// <summary>
        /// Parses headers, analyzes their binding projections, and publishes generated C# only after candidate emission succeeds.
        /// </summary>
        /// <param name="headerFile">
        /// The C header to parse.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful candidate emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Headers permitted to contribute declarations, or null to derive them from entries and configured transitive includes.
        /// </param>
        /// <returns>
        /// True when generation succeeds; false for parser, safety, unsupported-declaration, or publication diagnostics. lastResult describes the completed attempt.
        /// </returns>
        public bool Generate(
            string headerFile,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            return Generate([headerFile], outputPath, allowedHeaders);
        }

        /// <summary>
        /// Parses headers, analyzes their binding projections, and publishes generated C# only after candidate emission succeeds.
        /// </summary>
        /// <param name="headerFiles">
        /// The nonempty ordered C header entry list.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful candidate emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Headers permitted to contribute declarations, or null to derive them from entries and configured transitive includes.
        /// </param>
        /// <returns>
        /// True when generation succeeds; false for parser, safety, unsupported-declaration, or publication diagnostics. lastResult describes the completed attempt.
        /// </returns>
        public bool Generate(
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            BeginAttempt();
            return GenerateParsed(PrepareSettings(), headerFiles, outputPath, allowedHeaders);
        }

        /// <summary>
        /// Generates all configured entry files using paths relative to the loaded configuration file.
        /// </summary>
        /// <param name = "outputPath">Optional output path override. Relative paths use the configuration directory.</param>
        /// <returns><see langword="true"/> when parsing and generation complete without fatal diagnostics; otherwise <see langword="false"/>.</returns>
        public bool GenerateConfigured(string? outputPath = null)
        {
            BeginAttempt();
            ConfiguredGenerationRequest request = ConfiguredGenerationRequestResolver.Resolve(config, outputPath);
            CppParserOptions options = PrepareSettings();
            IncrementalCacheKey? cacheKey = null;
            IncrementalGenerationCache? cache = null;
            if (CanUseIncrementalCache())
            {
                string cachePath = Path.GetFullPath(config.cacheDirectory, request.baseDirectory);
                cache = new(cachePath);
                cacheKey = CreateCacheKey(request, options, cachePath);
                if (cache.TryRestore(cacheKey, request.outputPath, out string stateJson))
                {
                    CachedGenerationState state = JsonConvert.DeserializeObject<CachedGenerationState>(stateJson) ?? throw new InvalidDataException("Incremental C# binding cache metadata is invalid.");
                    this.lastResult = new(state.module, true, EnumerateOutputFiles(request.outputPath), state.diagnostics, true, cacheKey.value);
                    LogInfo($"Restored generated bindings from cache {cacheKey.value[..12]}.");
                    return true;
                }
            }

            bool success = GenerateParsed(options, request.headerFiles.ToList(), request.outputPath, request.allowedHeaders?.ToList());
            if (success && cache != null && cacheKey != null && this.lastResult != null)
            {
                cache.Store(cacheKey, request.outputPath, JsonConvert.SerializeObject(new CachedGenerationState(this.lastResult.module, this.lastResult.diagnostics.ToArray())));
                this.lastResult = new(this.lastResult.module, true, this.lastResult.outputFiles, this.lastResult.diagnostics, false, cacheKey.value);
            }

            return success;
        }

        /// <summary>
        /// Parses and analyzes configured headers without emitting or replacing generated output.
        /// </summary>
        /// <returns>A structured result containing the shared binding IR and parser diagnostics.</returns>
        public BindingGenerationResult<BindingModule> AnalyzeConfigured()
        {
            BeginAttempt();
            ConfiguredGenerationRequest request = ConfiguredGenerationRequestResolver.Resolve(config, null);
            ConfigValidator.Validate(config);
            LogInfo($"Analyzing: {config.apiName}");
            CppParserOptions options = PrepareSettings();
            using CppCompilation compilation = ParseFiles(options, request.headerFiles.ToList());
            try
            {
                return AnalyzeCore(compilation, request, options);
            }
            finally
            {
                config.typeConverter.ReleaseCompilation();
            }
        }

        private BindingGenerationResult<BindingModule> AnalyzeCore(
            CppCompilation compilation,
            ConfiguredGenerationRequest request,
            CppParserOptions options
        ) {
            LogCompilationDiagnostics(compilation);
            List<string> allowedHeaders = request.allowedHeaders?.ToList() ?? (config.includeTransitivelyReferencedHeaders ? ResolveTransitiveUserHeaders(compilation, request.headerFiles, options.includeFolders) : request.headerFiles.ToList());
            if (compilation.hasErrors)
            {
                this.lastResult = this.m_pipeline.CreateResult(compilation, allowedHeaders, false, request.outputPath, this.messages);
                return this.lastResult;
            }

            ParseResult result = this.m_pipeline.PrepareModel(this, compilation, new FileSet(allowedHeaders));
            BindingModule module = this.m_pipeline.Analyze(result, allowedHeaders);
            bool safetySuccess = !module.structuredDiagnostics.Any(diagnostic => diagnostic.severity == BindingDiagnosticSeverity.Error);
            this.lastResult = new(module, safetySuccess, [], [.. this.messages.Select(diagnostic => new BindingDiagnostic((BindingDiagnosticSeverity)(int)diagnostic.severity, diagnostic.message)), .. module.structuredDiagnostics]);
            return this.lastResult;
        }

        /// <summary>
        /// Parses headers, analyzes their binding projections, and publishes generated C# only after candidate emission succeeds.
        /// </summary>
        /// <param name="parserOptions">
        /// The explicit parsing and native target options borrowed for this attempt.
        /// </param>
        /// <param name="headerFile">
        /// The C header to parse.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful candidate emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Headers permitted to contribute declarations, or null to derive them from the entry list.
        /// </param>
        /// <returns>
        /// True when generation succeeds; false for parser, safety, unsupported-declaration, or publication diagnostics. lastResult describes the completed attempt.
        /// </returns>
        public bool Generate(
            CppParserOptions parserOptions,
            string headerFile,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            return Generate(parserOptions, [headerFile], outputPath, allowedHeaders);
        }

        /// <summary>
        /// Parses headers, analyzes their binding projections, and publishes generated C# only after candidate emission succeeds.
        /// </summary>
        /// <param name="parserOptions">
        /// The explicit parsing and native target options borrowed for this attempt.
        /// </param>
        /// <param name="headerFiles">
        /// The nonempty ordered C header entry list.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful candidate emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Headers permitted to contribute declarations, or null to derive them from the entry list.
        /// </param>
        /// <returns>
        /// True when generation succeeds; false for parser, safety, unsupported-declaration, or publication diagnostics. lastResult describes the completed attempt.
        /// </returns>
        public bool Generate(
            CppParserOptions parserOptions,
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            BeginAttempt();
            return GenerateParsed(parserOptions, headerFiles, outputPath, allowedHeaders);
        }

        private bool GenerateParsed(
            CppParserOptions parserOptions,
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders
        ) {
            ArgumentNullException.ThrowIfNull(parserOptions);
            ArgumentNullException.ThrowIfNull(headerFiles);
            if (headerFiles.Count == 0)
            {
                throw new ArgumentException("At least one header file is required.", nameof(headerFiles));
            }

            ConfigureCore();
            LogInfo($"Generating: {config.apiName}");
            LogInfo("Parsing Headers...");
            using CppCompilation compilation = ParseFiles(parserOptions, headerFiles);
            try
            {
                return GenerateCore(compilation, headerFiles, outputPath, allowedHeaders);
            }
            finally
            {
                config.typeConverter.ReleaseCompilation();
            }
        }

        /// <summary>
        /// Resets attempt-local preprocessing, configures derived steps and then notifies configuration subscribers.
        /// </summary>
        protected virtual void ConfigureCore()
        {
            preProcessSteps.Clear();
            ConfigureGeneratorCore(this.preProcessSteps);
            PostConfigure?.Invoke(this, config);
        }

        /// <summary>
        /// Adds standard constant preprocessing before invoking the derived configuration hook.
        /// </summary>
        /// <param name="preProcessSteps">Mutable step list owned by this attempt; overrides append their preprocessing steps here.</param>
        protected virtual void ConfigureGeneratorCore(List<PreProcessStep> preProcessSteps)
        {
            preProcessSteps.Add(new ConstantPreProcessStep(this, config));
            OnConfigureGenerator();
        }

        /// <summary>
        /// Allows derived generators to append preprocessing behavior before this attempt starts.
        /// </summary>
        protected virtual void OnConfigureGenerator()
        {
        }

        /// <summary>
        /// Resolves target and configuration paths into parser options for this attempt.
        /// </summary>
        /// <returns>Fresh parser options; this method neither parses input nor transfers configuration ownership.</returns>
        protected virtual CppParserOptions PrepareSettings()
        {
            string baseDirectory = config.configDirectory ?? Environment.CurrentDirectory;
            var options = new CppParserOptions
            {
                parseMacros = config.parseMacros,
                parseComments = config.parseComments,
                parseSystemIncludes = config.parseSystemIncludes,
                parseCommentAttribute = config.parseComments,
                //ParseTokenAttributes = true,
                parserKind = config.parserKind,
                autoSquashTypedef = config.autoSquashTypedef,
            };
            options.ConfigureForTarget(config.resolvedTarget);
            var additionalArguments = config.additionalArguments ?? [];
            var includeFolders = config.includeFolders ?? [];
            var systemIncludeFolders = config.systemIncludeFolders ?? [];
            var defines = config.defines ?? [];
            for (int i = 0; i < additionalArguments.Count; i++)
            {
                options.additionalArguments.Add(additionalArguments[i]);
            }

            for (int i = 0; i < includeFolders.Count; i++)
            {
                options.includeFolders.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(includeFolders[i]), baseDirectory));
            }

            for (int i = 0; i < systemIncludeFolders.Count; i++)
            {
                options.systemIncludeFolders.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(systemIncludeFolders[i]), baseDirectory));
            }

            for (int i = 0; i < defines.Count; i++)
            {
                options.defines.Add(defines[i]);
            }

            //options.AdditionalArguments.Add("-std=c++17");
            return options;
        }



        private bool CanUseIncrementalCache() => config.enableIncrementalCache && GetType() == typeof(CsCodeGenerator);
        private void BeginAttempt()
        {
            lastResult = null;
            ResetDiagnostics();
            preProcessSteps.Clear();
            config.definedCppEnums = [];
            m_metadata = new() { settings = config };
            logLevel = config.logLevel;
        }
        private IncrementalCacheKey CreateCacheKey(
            ConfiguredGenerationRequest request,
            CppParserOptions options,
            string cachePath
        ) {
            IReadOnlyList<string> inputs = IncrementalGenerationCache.DiscoverInputs(request.headerFiles, options.includeFolders.Concat(options.systemIncludeFolders),
                [request.outputPath, cachePath, Path.GetFullPath(config.outputPath, request.baseDirectory)]);
            string assemblyIdentity = GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D");
            string optionsFingerprint = string.Join("\n", new[] { options.targetTriple }.Concat(options.additionalArguments).Concat(options.defines).Concat(options.systemIncludeFolders));
            string fingerprint = "csharp\n" + assemblyIdentity + "\n" + config.Serialize() + "\n" + optionsFingerprint + "\n" + CppToolchainDiscovery.GetCompilerFingerprint(config.parserKind, ConfigurationPath.Resolve(config.compilerPath ?? config.resolvedTarget.toolchain.compilerPath, request.baseDirectory, allowCommandName: true)) + "\n" + config.plugins.GetCacheFingerprint();
            return IncrementalGenerationCache.CreateKey(fingerprint, inputs);
        }

        private static string[] EnumerateOutputFiles(string outputPath) => Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        private sealed record CachedGenerationState(
            BindingModule? module,
            BindingDiagnostic[] diagnostics
        );
        /// <summary>
        /// Parses the selected headers and transfers compilation ownership to the generation operation.
        /// </summary>
        /// <param name="parserOptions">Resolved target, search paths and parser policy.</param>
        /// <param name="headerFiles">Headers to parse together.</param>
        /// <returns>A newly owned compilation, disposed by the generator after analysis and emission, including failure paths.</returns>
        protected virtual CppCompilation ParseFiles(
            CppParserOptions parserOptions,
            List<string> headerFiles
        ) {
            return CppParser.ParseFiles(headerFiles, parserOptions);
        }

        private bool GenerateCore(
            CppCompilation compilation,
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            return this.m_pipeline.Generate(this, compilation, headerFiles, outputPath, allowedHeaders);
        }

        internal CsCodeGeneratorConfig pipelineConfig => config;
        internal CsCodeGeneratorMetadata pipelineMetadata => this.m_metadata;

        internal void SetLastResult(BindingGenerationResult<BindingModule> result) => this.lastResult = result;
        internal void ReportCompilationDiagnostics(CppCompilation compilation) => LogCompilationDiagnostics(compilation);
        internal List<string> ResolveAllowedHeaders(
            CppCompilation compilation,
            IReadOnlyList<string> headerFiles
        ) {
            string baseDirectory = config.configDirectory ?? Environment.CurrentDirectory;
            string[] includeFolders = config.includeFolders.Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), baseDirectory)).ToArray();
            return config.includeTransitivelyReferencedHeaders ? ResolveTransitiveUserHeaders(compilation, headerFiles, includeFolders) : [.. headerFiles];
        }

        internal void InvokePrePatch(ParseResult result)
        {
            LogInfo("Applying Pre-Patches...");
            patchEngine.ApplyPrePatches(config, result);
            OnPrePatch(result);
        }
        internal void RewriteGeneratedRuntimeUsings(string outputPath) => RewriteRuntimeUsings(outputPath);
        internal static void RemoveEmptyGeneratedTypes(string outputPath) => RemoveEmptyPartialTypes(outputPath);
        internal static void RemoveEmptyGeneratedDirectories(string outputPath) => DeleteEmptyDirectories(outputPath);
        internal void ComposeSingleFile(string generationOutputPath) => MergeGeneratedFilesToSingleFile(generationOutputPath, generationOutputPath);
        internal void EmitStandaloneRuntime(string outputPath) => WriteStandaloneRuntimeFile(outputPath);
        private void LogCompilationDiagnostics(CppCompilation compilation)
        {
            for (int i = 0; i < compilation.diagnostics.messages.Count; i++)
            {
                CppDiagnosticMessage message = compilation.diagnostics.messages[i];
                LogSeverity severity = message.type switch
                {
                    CppLogMessageType.Error => LogSeverity.Error,
                    CppLogMessageType.Warning => LogSeverity.Warning,
                    _ => LogSeverity.Information
                };
                RecordDiagnostic(severity, message.ToString(), config.cppLogLevel);
            }
        }

        private static List<string> ResolveTransitiveUserHeaders(
            CppCompilation compilation,
            IReadOnlyList<string> headerFiles,
            IReadOnlyList<string> includeFolders
        ) {
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

            foreach (string candidate in IncrementalGenerationCache.DiscoverInputs([], roots))
            {
                string extension = Path.GetExtension(candidate);
                if (extension is ".h" or ".hh" or ".hpp" or ".hxx" or ".inc")
                    sources.Add(candidate);
            }

            foreach (CppMacro macro in compilation.macros)
            {
                AddSource(macro.sourceFile);
            }

            Collect(compilation);
            return [.. sources];
            void Collect(ICppContainer container)
            {
                if (!visited.Add(container))
                {
                    return;
                }

                foreach (ICppDeclaration declaration in container.children)
                {
                    if (declaration is CppElement element)
                    {
                        AddSource(element.sourceFile);
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
                    if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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

                if (line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(';') && !line.Contains('='))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Composes staged C# sources and removes their separate parts after the merged file is written.
        /// </summary>
        /// <param name="generationOutputPath">Candidate source directory, never the previous published output.</param>
        /// <param name="outputPath">Candidate directory receiving the merged file.</param>
        protected virtual void MergeGeneratedFilesToSingleFile(
            string generationOutputPath,
            string outputPath
        ) {
            string mergedPath = Path.Combine(outputPath, SingleFileOutputNameResolver.Resolve(config));
            List<string> files = Directory.GetFiles(generationOutputPath, "*.cs", SearchOption.AllDirectories).Where(path => !string.Equals(path, mergedPath, StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0)
                return;
            new SingleFileComposer().Compose(files, mergedPath, config.@namespace, config.resolvedTarget.targetId.value);
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
                if (!text.Contains(global::BGCS.Facade.CsCodeGenerator.C_RUNTIMEUSINGDEFAULT, StringComparison.Ordinal))
                {
                    continue;
                }

                text = text.Replace(global::BGCS.Facade.CsCodeGenerator.C_RUNTIMEUSINGDEFAULT, runtimeUsingTarget, StringComparison.Ordinal);
                text = text.Replace("Utils.", $"global::{GetRuntimeNamespace()}.Utils.", StringComparison.Ordinal);
                File.WriteAllText(path, text);
            }
        }

        private void WriteStandaloneRuntimeFile(string outputPath)
        {
            BindingModule module = new(config.apiName, config.@namespace, config.libName, config.resolvedTarget.targetId.value);
            string runtimeNamespace = GetRuntimeNamespace();
            string runtimePath = new RuntimeEmitter().Emit(module, new(outputPath, true, "Runtime.cs", runtimeNamespace)).Single();
            LogInfo($"Generated runtime file: {runtimePath}");
        }

        private string GetRuntimeNamespace()
        {
            return string.IsNullOrWhiteSpace(config.runtimeNamespace) ? "BGCS.Runtime" : config.runtimeNamespace;
        }

        private static void DeleteEmptyDirectories(string rootPath)
        {
            string[] dirs = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
            Array.Sort(dirs, (
                a,
                b
            ) => b.Length.CompareTo(a.Length));
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

        /// <summary>
        /// Allows a derived generator to transform borrowed analysis after registered pre-patches.
        /// </summary>
        /// <param name="result">
        /// The attempt-local model; native declarations must not be retained beyond this generation attempt.
        /// </param>
        protected virtual void OnPrePatch(ParseResult result)
        {
        }

        /// <summary>
        /// Clears attempt diagnostics, preprocessing steps, the last structured result, and generated metadata while retaining configuration and registered patches.
        /// </summary>
        public virtual void Reset()
        {
            BeginAttempt();
        }

        /// <summary>
        /// Writes the current mutable metadata as indented JSON for inspection or downstream tooling; this operation does not publish generated bindings.
        /// </summary>
        /// <param name="path">
        /// The destination file; its parent directory must already exist.
        /// </param>
        /// <exception cref="System.IO.IOException">
        /// The destination cannot be written.
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// The destination does not permit writing.
        /// </exception>
        public void SaveMetadata(string path)
        {
            JsonSerializerSettings options = new()
            {
                Formatting = Formatting.Indented
            };
            var json = JsonConvert.SerializeObject(this.m_metadata, options);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Returns the retained mutable metadata of the current generation attempt without copying it.
        /// </summary>
        /// <returns>
        /// The live attempt metadata; a later attempt or Reset replaces the container rather than updating previously returned references.
        /// </returns>
        public CsCodeGeneratorMetadata GetMetadata()
        {
            return this.m_metadata;
        }
    }
}
