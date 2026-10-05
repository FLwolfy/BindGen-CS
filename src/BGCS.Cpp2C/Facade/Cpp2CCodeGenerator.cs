using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Cpp2C.Analysis;

namespace BGCS.Cpp2C.Facade
{
    using BGCS.Core.Caching;
    using BGCS.Core.IO;
    using BGCS.Core.Logging;
    using BGCS.Cpp2C.Application;
    using BGCS.Cpp2C.Build;
    using BGCS.Cpp2C.Configuration;
    using BGCS.Cpp2C.Lowering;
    using BGCS.CppAst.Diagnostics;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Parsing;
    using BGCS.CppAst.Targeting;
    using BGCS.Intermediate;
    using BGCS.Intermediate.Bridges;
    using Newtonsoft.Json;

    /// <summary>
    /// Owns C++ facade parsing and lowering into a frozen bridge module, then atomically publishes its C header and C++ implementation.
    /// </summary>
    public partial class Cpp2CCodeGenerator : BaseGenerator
    {
        private bool m_loweringsApplied;
        /// <summary>
        /// Retains the bridge configuration whose target, plugins, and lowering recipes are used by subsequent attempts.
        /// </summary>
        /// <param name="settings">
        /// The mutable configuration retained by this generator; its plugin lifetime belongs to the caller.
        /// </param>
        public Cpp2CCodeGenerator(Cpp2CGeneratorConfig settings) : base(settings)
        {
        }

        /// <summary>
        /// Gets the structured result of the most recent C++ bridge generation attempt.
        /// </summary>
        public BindingGenerationResult<CppBridgeModule>? lastResult { get; private set; }

        /// <summary>
        /// Applies lowering services and resolves validated target options for one C++ parse.
        /// </summary>
        /// <returns>Fresh C++ parser options borrowing the configured target toolchain.</returns>
        protected virtual CppParserOptions PrepareSettings()
        {
            ApplyLoweringServices();
            Cpp2CConfigValidator.Validate(config);
            string baseDirectory = config.configDirectory ?? Environment.CurrentDirectory;
            var options = new CppParserOptions
            {
                parseMacros = config.parseMacros,
                parseComments = config.parseComments,
                parseSystemIncludes = config.parseSystemIncludes,
                parserKind = CppParserKind.Cpp,
                autoSquashTypedef = true,
            };
            options.ConfigureForTarget(config.resolvedTarget);
            for (int i = 0; i < config.additionalArguments.Count; i++)
            {
                options.additionalArguments.Add(config.additionalArguments[i]);
            }

            for (int i = 0; i < config.includeFolders.Count; i++)
            {
                options.includeFolders.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(config.includeFolders[i]), baseDirectory));
            }

            for (int i = 0; i < config.systemIncludeFolders.Count; i++)
            {
                options.systemIncludeFolders.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(config.systemIncludeFolders[i]), baseDirectory));
            }

            for (int i = 0; i < config.defines.Count; i++)
            {
                options.defines.Add(config.defines[i]);
            }

            if (!options.additionalArguments.Any(argument => argument.StartsWith("-std=", StringComparison.Ordinal)))
                options.additionalArguments.Add("-std=" + config.languageStandard);
            List<string> explicitInstantiations = [..config.templateInstantiations.Select(type => $"template class {type};"), ..config.functionTemplateInstantiations.Select((
                declaration,
                index
            ) => BuildFunctionTemplateForwarder(declaration, index))];
            if (explicitInstantiations.Count > 0)
                options.postHeaderText = string.Join(Environment.NewLine, explicitInstantiations);
            return options;
        }



        private static string BuildFunctionTemplateForwarder(
            string declaration,
            int index
        ) {
            int separator = declaration.IndexOf(' ');
            int open = declaration.LastIndexOf('(');
            int close = declaration.LastIndexOf(')');
            if (separator <= 0 || open <= separator || close <= open)
                throw new InvalidOperationException($"Invalid function-template instantiation declaration '{declaration}'. Expected 'ReturnType Namespace::Function<Args>(ParameterTypes)'.");
            string returnType = declaration[..separator].Trim();
            string function = declaration[(separator + 1)..open].Trim();
            string parametersText = declaration[(open + 1)..close].Trim();
            List<string> parameterTypes = SplitTemplateParameters(parametersText);
            string parameters = string.Join(", ", parameterTypes.Select((
                type,
                parameterIndex
            ) => $"{type} arg{parameterIndex}"));
            string arguments = string.Join(", ", parameterTypes.Select((
                _,
                parameterIndex
            ) => $"arg{parameterIndex}"));
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
                if (parameters[i] == '<')
                    depth++;
                else if (parameters[i] == '>')
                    depth--;
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
        /// <param name = "outputPath">Optional output override, relative to the configuration directory unless absolute.</param>
        public void GenerateConfigured(string? outputPath = null)
        {
            BeginAttempt();
            if (config.entryFiles.Count == 0)
                throw new InvalidOperationException("Cpp2C configuration requires at least one EntryFiles item.");
            string baseDirectory = config.configDirectory ?? Environment.CurrentDirectory;
            List<string> headers = config.entryFiles.Select(path => Path.GetFullPath(path, baseDirectory)).ToList();
            List<string>? allowedHeaders = config.allowedHeaders.Count == 0 ? null : config.allowedHeaders.Select(path => Path.GetFullPath(path, baseDirectory)).ToList();
            string resolvedOutput = Path.GetFullPath(outputPath ?? config.outputPath, baseDirectory);
            ApplyLoweringServices();
            CppParserOptions options = PrepareSettings();
            IncrementalGenerationCache? cache = null;
            IncrementalCacheKey? cacheKey = null;
            if (CanUseIncrementalCache())
            {
                string cachePath = Path.GetFullPath(config.cacheDirectory, baseDirectory);
                cache = new(cachePath);
                IReadOnlyList<string> inputs = IncrementalGenerationCache.DiscoverInputs(headers, options.includeFolders.Concat(options.systemIncludeFolders),
                    [resolvedOutput, cachePath, Path.GetFullPath(config.outputPath, baseDirectory)]);
                string fingerprint = "cpp-bridge\n" + GetType().Assembly.ManifestModule.ModuleVersionId.ToString("D") + "\n" + config.Serialize() + "\n" + string.Join("\n", new[] { options.targetTriple }.Concat(options.additionalArguments).Concat(options.defines).Concat(options.systemIncludeFolders)) + "\n" + CppToolchainDiscovery.GetCompilerFingerprint(CppParserKind.Cpp, ConfigurationPath.Resolve(config.compilerPath, baseDirectory, allowCommandName: true)) + "\n" + config.plugins.GetCacheFingerprint() + "\n" + (config.lowerings.TryGetCacheFingerprint(out string loweringFingerprint) ? loweringFingerprint : "lowerings:unfingerprinted");
                cacheKey = IncrementalGenerationCache.CreateKey(fingerprint, inputs);
                if (cache.TryRestore(cacheKey, resolvedOutput, out string stateJson))
                {
                    CachedGenerationState state = JsonConvert.DeserializeObject<CachedGenerationState>(stateJson) ?? throw new InvalidDataException("Incremental C++ bridge cache metadata is invalid.");
                    this.lastResult = new(state.module, true, EnumerateOutputFiles(resolvedOutput), state.diagnostics, true, cacheKey.value);
                    LogInfo($"Restored generated C++ bridge from cache {cacheKey.value[..12]}.");
                    return;
                }
            }

            Generate(options, headers, resolvedOutput, allowedHeaders);
            if (this.lastResult?.success == true && cache != null && cacheKey != null)
            {
                cache.Store(cacheKey, resolvedOutput, JsonConvert.SerializeObject(new CachedGenerationState(this.lastResult.module, this.lastResult.diagnostics.ToArray())));
                this.lastResult = new(this.lastResult.module, true, this.lastResult.outputFiles, this.lastResult.diagnostics, false, cacheKey.value);
            }
        }

        /// <summary>
        /// Analyzes and lowers facade declarations into frozen bridge IR before publishing the C bridge; lastResult records success or failure.
        /// </summary>
        /// <param name="headerFile">
        /// The C++ facade entry header.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful bridge emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Additional headers permitted to contribute declarations, or null to use only the entry header.
        /// </param>
        public virtual void Generate(
            string headerFile,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            Generate([headerFile], outputPath, allowedHeaders);
        }

        /// <summary>
        /// Analyzes and lowers facade declarations into frozen bridge IR before publishing the C bridge; lastResult records success or failure.
        /// </summary>
        /// <param name="headerFiles">
        /// The ordered C++ facade entry headers.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful bridge emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Additional headers permitted to contribute declarations, or null to use only the entry headers.
        /// </param>
        public virtual void Generate(
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders = null
        ) {
            BeginAttempt();
            var options = PrepareSettings();
            Generate(options, headerFiles, outputPath, allowedHeaders);
        }

        private void Generate(
            CppParserOptions options,
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders
        ) {
            using var compilation = CppParser.ParseFiles(headerFiles, options);
            GenerateCore(compilation, headerFiles, outputPath, allowedHeaders);
        }

        /// <summary>
        /// Analyzes and lowers facade declarations into frozen bridge IR before publishing the C bridge; lastResult records success or failure.
        /// </summary>
        /// <param name="compilation">
        /// The caller-owned parsed compilation, borrowed and retained only for this attempt; this method does not dispose it.
        /// </param>
        /// <param name="headerFiles">
        /// The entry headers used to identify declarations and build-manifest inputs.
        /// </param>
        /// <param name="outputPath">
        /// The destination output directory replaced only after successful bridge emission.
        /// </param>
        /// <param name="allowedHeaders">
        /// Additional headers permitted to contribute declarations, or null to use only the entry headers.
        /// </param>
        public virtual void Generate(
            CppCompilation compilation,
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders
        ) {
            BeginAttempt();
            GenerateCore(compilation, headerFiles, outputPath, allowedHeaders);
        }

        private void BeginAttempt()
        {
            lastResult = null;
            ResetDiagnostics();
            logLevel = config.logLevel;
        }

        private void GenerateCore(
            CppCompilation compilation,
            List<string> headerFiles,
            string outputPath,
            List<string>? allowedHeaders
        ) {
            ArgumentNullException.ThrowIfNull(compilation);
            ArgumentNullException.ThrowIfNull(headerFiles);
            ApplyLoweringServices();
            Cpp2CConfigValidator.Validate(config);
            for (int i = 0; i < compilation.diagnostics.messages.Count; i++)
            {
                CppDiagnosticMessage? message = compilation.diagnostics.messages[i];
                LogSeverity severity = message.type switch
                {
                    CppLogMessageType.Error => LogSeverity.Error,
                    CppLogMessageType.Warning => LogSeverity.Warning,
                    _ => LogSeverity.Information
                };
                RecordDiagnostic(severity, message.ToString(), config.cppLogLevel);
            }

            if (compilation.hasErrors)
            {
                this.lastResult = new(null, false, [], CreateDiagnostics());
                return;
            }

            List<string> effectiveAllowedHeaders = allowedHeaders == null ? [.. headerFiles] : [.. allowedHeaders, .. headerFiles];
            if (config.templateInstantiations.Count > 0 || config.functionTemplateInstantiations.Count > 0)
                effectiveAllowedHeaders.Add(CppParser.C_CPPASTROOTFILENAME);
            FileSet files = new(effectiveAllowedHeaders);
            using OutputDirectoryTransaction outputTransaction = new(outputPath);
            string generationOutputPath = outputTransaction.stagingPath;
            Directory.CreateDirectory(Path.Combine(generationOutputPath, "include"));
            Directory.CreateDirectory(Path.Combine(generationOutputPath, "src"));
            try
            {
                ParseResult result = new(compilation, headerFiles);
                CppBridgeModule module = new CppBridgeGenerationPipeline(config).Generate(files, result, generationOutputPath);
                if (config.generateBuildManifest)
                    CppBridgeBuildManifestEmitter.Emit(config, headerFiles, generationOutputPath);
                outputTransaction.Commit();
                this.lastResult = new(module, true, Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray(), [.. CreateDiagnostics(), .. module.diagnostics]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                string failure = "C++ bridge output could not be written or published: " + exception;
                LogError(failure);
                this.lastResult = new(null, false, [], [.. CreateDiagnostics(), new(BindingDiagnosticSeverity.Error, failure, BindingDiagnosticCodes.C_OUTPUTFAILURE)]);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                string remediation = BuildUnsupportedRemediation(exception);
                LogError(remediation);
                this.lastResult = new(null, false, [], [.. CreateDiagnostics(), new(BindingDiagnosticSeverity.Error, remediation, BindingDiagnosticCodes.C_CPPUNSUPPORTED)]);
            }
        }

        private static string BuildUnsupportedRemediation(Exception exception)
        {
            return $"C++ bridge generation rejected an unsupported declaration: {exception.Message} " + "Add a declarative TypeLowerings recipe, request a concrete class through TemplateInstantiations, " + "register a typed lowering plugin, or provide an explicit NativeShims C ABI boundary with ownership and allocator semantics.";
        }

        private BindingDiagnostic[] CreateDiagnostics()
        {
            return this.messages.Select(message => new BindingDiagnostic((BindingDiagnosticSeverity)(int)message.severity, message.message)).ToArray();
        }

        private void ApplyLoweringServices()
        {
            if (this.m_loweringsApplied)
                return;
            foreach (CppTypeLoweringRecipe recipe in config.typeLowerings)
                config.lowerings.Register(new ConfiguredCppTypeLowering(recipe));
            foreach (CppCallableLoweringRecipe recipe in config.callableLowerings)
                config.lowerings.Register(new ConfiguredCppCallableLowering(recipe));
            string configDirectory = config.configDirectory ?? Environment.CurrentDirectory;
            foreach (CppNativeShim shim in config.nativeShims)
                config.lowerings.Register(new ConfiguredNativeShimContributor(shim, configDirectory));
            foreach (var registration in config.plugins.GetServices<ICppTypeLowering>())
                config.lowerings.Register(registration.service);
            foreach (var registration in config.plugins.GetServices<ICppCallableLowering>())
                config.lowerings.Register(registration.service);
            foreach (var registration in config.plugins.GetServices<ICppArtifactContributor>())
                config.lowerings.Register(registration.service);
            this.m_loweringsApplied = true;
        }

        private bool CanUseIncrementalCache() => config.enableIncrementalCache && GetType() == typeof(Cpp2CCodeGenerator) && config.lowerings.TryGetCacheFingerprint(out _);
        private static string[] EnumerateOutputFiles(string outputPath) => Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        private sealed record CachedGenerationState(
            CppBridgeModule? module,
            BindingDiagnostic[] diagnostics
        );
    }
}
