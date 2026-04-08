namespace BGCS
{
    using BGCS.Core;
    using BGCS.Core.CSharp;
    using BGCS.Core.Logging;
    using BGCS.CppAst.Diagnostics;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Model.Types;
    using BGCS.CppAst.Parsing;
    using BGCS.FunctionGeneration;
    using BGCS.GenerationSteps;
    using BGCS.Metadata;
    using BGCS.Patching;
    using BGCS.PreProcessSteps;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Defines the public class <c>CsCodeGenerator</c> used by the generation pipeline.
    /// </summary>
    public partial class CsCodeGenerator : BaseGenerator
    {
        private const string RuntimeUsingDefault = "using BGCS.Runtime;";
        private const string RuntimeSourceExcludeSymbol = "BGCS_RUNTIME_EXTERNAL";
        private static readonly Regex EmptyPartialTypeRegex = new(
            @"^\s*public\s+(?:static\s+)?(?:readonly\s+)?(?:unsafe\s+)?partial\s+(?:class|struct)\s+\w+\s*\r?\n\s*\{\s*\r?\n\s*\}\s*(?:\r?\n)?",
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

        /// <summary>
        /// Performs the operation implemented by <c>Create</c>.
        /// </summary>
        /// <returns>Result produced by <c>Create</c>.</returns>
        public static CsCodeGenerator Create(string configPath)
        {
            return new(CsCodeGeneratorConfig.Load(configPath));
        }

        /// <summary>
        /// Performs the operation implemented by <c>CsCodeGenerator</c>.
        /// </summary>
        /// <returns>Result produced by <c>CsCodeGenerator</c>.</returns>
        public CsCodeGenerator(CsCodeGeneratorConfig config) : base(config)
        {
        }

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
            var options = new CppParserOptions
            {
                ParseMacros = true,
                ParseComments = true,
                ParseSystemIncludes = true,

                ParseCommentAttribute = true,
                //ParseTokenAttributes = true,
                ParserKind = CppParserKind.Cpp,

                AutoSquashTypedef = config.AutoSquashTypedef,
            };

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
                options.IncludeFolders.Add(includeFolders[i]);
            }

            for (int i = 0; i < systemIncludeFolders.Count; i++)
            {
                options.SystemIncludeFolders.Add(systemIncludeFolders[i]);
            }

            for (int i = 0; i < defines.Count; i++)
            {
                options.Defines.Add(defines[i]);
            }

            // options.ConfigureForWindowsMsvc(CppTargetCpu.X86_64);
            //options.AdditionalArguments.Add("-std=c++17");

            return options;
        }

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
            if (CLIOptions != null && CLIOptions.OutputDirectory != null)
            {
                outputPath = Path.Combine(CLIOptions.OutputDirectory, outputPath);
            }
            else
            {
            }

            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            Directory.CreateDirectory(outputPath);

            bool singleFileOutputOnly = config.MergeGeneratedFilesToSingleFile;
            string generationOutputPath = outputPath;
            if (singleFileOutputOnly)
            {
                generationOutputPath = Path.Combine(Path.GetTempPath(), "bgcs-staging-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(generationOutputPath);
            }

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
                return false;
            }

            if (allowedHeaders is null)
            {
                allowedHeaders = [.. headerFiles];
            }

            bool explicitEmptyOutputFilter = allowedHeaders.Count == 0;
            FileSet files = new(allowedHeaders.Select(PathHelper.GetPath));

            foreach (var meta in copyFromPending)
            {
                foreach (var step in generationSteps)
                {
                    step.CopyFromMetadata(meta);
                }
            }

            LogInfo($"Configuring Pre-Processing Steps...");
            foreach (var step in PreProcessSteps)
            {
                step.Configure(config);
            }

            LogInfo("Running Pre-Processing Steps...");

            ParseResult result = new(compilation);
            config.TypeConverter.Initialize(result);
            foreach (var step in PreProcessSteps)
            {
                step.PreProcess(files, compilation, config, metadata, result);
            }

            OnPrePatchCore(result, headerFiles);

            LogInfo($"Configuring Steps...");
            foreach (var step in GenerationSteps)
            {
                step.Configure(config);
            }

            foreach (var step in GenerationSteps)
            {
                if (step.Enabled)
                {
                    if (explicitEmptyOutputFilter)
                    {
                        continue;
                    }

                    LogInfo($"Generating {step.Name}...");
                    step.Generate(files, result, generationOutputPath, config, metadata);
                    step.CopyToMetadata(metadata);
                }
            }

            LogInfo("Applying Post-Patches...");
            patchEngine.ApplyPostPatches(metadata, generationOutputPath, Directory.GetFiles(generationOutputPath, "*.*", SearchOption.AllDirectories).ToList());

            RewriteRuntimeUsings(generationOutputPath);
            RemoveEmptyPartialTypes(generationOutputPath);
            DeleteEmptyDirectories(generationOutputPath);

            if (config.MergeGeneratedFilesToSingleFile)
            {
                MergeGeneratedFilesToSingleFile(generationOutputPath, outputPath);
            }

            if (config.GenerateRuntimeSource)
            {
                WriteStandaloneRuntimeFile(outputPath);
            }

            if (singleFileOutputOnly && Directory.Exists(generationOutputPath))
            {
                Directory.Delete(generationOutputPath, true);
            }

            return true;
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
            string mergedPath = Path.Combine(outputPath, "Bindings.cs");

            List<string> files = Directory
                .GetFiles(generationOutputPath, "*.cs", SearchOption.AllDirectories)
                .Where(x => !string.Equals(x, mergedPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                return;
            }

            string? headerBanner = null;
            List<string> orderedUsings = [];
            HashSet<string> usingSet = new(StringComparer.Ordinal);
            List<string> bodies = [];
            bool canWrapSingleNamespace = true;

            for (int i = 0; i < files.Count; i++)
            {
                string text = File.ReadAllText(files[i]);
                ParsedMergedFile parsed = ParseMergedFile(text);

                if (headerBanner == null && !string.IsNullOrWhiteSpace(parsed.HeaderBanner))
                {
                    headerBanner = parsed.HeaderBanner;
                }

                for (int j = 0; j < parsed.Usings.Count; j++)
                {
                    string usingLine = parsed.Usings[j];
                    if (usingSet.Add(usingLine))
                    {
                        orderedUsings.Add(usingLine);
                    }
                }

                if (!string.IsNullOrWhiteSpace(parsed.Body))
                {
                    string sourceBody = parsed.Body.Replace("\r\n", "\n");
                    if (TryUnwrapNamespaceBody(sourceBody, config.Namespace, out string? unwrappedBody))
                    {
                        string normalizedBody = DedentLines(NormalizeMergedBody(unwrappedBody));
                        string bodyWithoutUsings = ExtractLeadingUsingLines(normalizedBody, orderedUsings, usingSet);
                        if (!string.IsNullOrWhiteSpace(bodyWithoutUsings))
                        {
                            bodies.Add(bodyWithoutUsings);
                        }
                    }
                    else
                    {
                        canWrapSingleNamespace = false;
                        string normalizedBody = NormalizeMergedBody(sourceBody);
                        string bodyWithoutUsings = ExtractLeadingUsingLines(normalizedBody, orderedUsings, usingSet);
                        if (!string.IsNullOrWhiteSpace(bodyWithoutUsings))
                        {
                            bodies.Add(bodyWithoutUsings);
                        }
                    }
                }
            }

            StringBuilder builder = new();
            if (!string.IsNullOrWhiteSpace(headerBanner))
            {
                builder.AppendLine(headerBanner.TrimEnd());
                builder.AppendLine();
            }

            for (int i = 0; i < orderedUsings.Count; i++)
            {
                builder.AppendLine(orderedUsings[i]);
            }

            if (orderedUsings.Count > 0 && bodies.Count > 0)
            {
                builder.AppendLine();
            }

            if (canWrapSingleNamespace && bodies.Count > 0)
            {
                builder.AppendLine($"namespace {config.Namespace}");
                builder.AppendLine("{");
                List<string> nonEmptyBodies = bodies
                    .Select(x => x.Trim('\r', '\n'))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                for (int i = 0; i < nonEmptyBodies.Count; i++)
                {
                    builder.AppendLine(IndentLines(nonEmptyBodies[i], "    "));
                    if (i + 1 < nonEmptyBodies.Count)
                    {
                        builder.AppendLine();
                    }
                }
                builder.AppendLine("}");
            }
            else
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    builder.AppendLine(bodies[i]);
                    if (i + 1 < bodies.Count)
                    {
                        builder.AppendLine();
                    }
                }
            }

            string mergedText = builder.ToString().TrimEnd() + Environment.NewLine;
            mergedText = NormalizeLeadingTabsPerLine(mergedText, 4);
            File.WriteAllText(mergedPath, mergedText);
            LogInfo($"Merged generated files into: {mergedPath}");

            for (int i = 0; i < files.Count; i++)
            {
                File.Delete(files[i]);
            }

            DeleteEmptyDirectories(generationOutputPath);
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

                File.WriteAllText(path, text);
            }
        }

        private void WriteStandaloneRuntimeFile(string outputPath)
        {
            IReadOnlyList<(string Name, string Content)> runtimeSources = GetEmbeddedRuntimeSources();
            if (runtimeSources.Count == 0)
            {
                runtimeSources = GetRuntimeSourcesFromFileSystem();
            }

            if (runtimeSources.Count == 0)
            {
                LogWarn("Runtime is required but no runtime sources were found.");
                return;
            }

            List<string> orderedUsings = [];
            HashSet<string> usingSet = new(StringComparer.Ordinal);
            List<string> bodies = [];
            string runtimeNamespace = GetRuntimeNamespace();
            bool canWrapSingleNamespace = true;
            for (int i = 0; i < runtimeSources.Count; i++)
            {
                string normalizedRuntimeText = NormalizeRuntimeTextForMerge(RewriteRuntimeNamespace(runtimeSources[i].Content));
                ParsedMergedFile parsedRuntime = ParseMergedFile(normalizedRuntimeText);

                for (int j = 0; j < parsedRuntime.Usings.Count; j++)
                {
                    string usingLine = parsedRuntime.Usings[j];
                    if (usingSet.Add(usingLine))
                    {
                        orderedUsings.Add(usingLine);
                    }
                }

                if (!string.IsNullOrWhiteSpace(parsedRuntime.Body))
                {
                    string sourceBody = parsedRuntime.Body.Replace("\r\n", "\n");
                    if (TryUnwrapNamespaceBody(sourceBody, runtimeNamespace, out string? unwrappedBody))
                    {
                        ParsedMergedFile parsedInner = ParseMergedFile(unwrappedBody);
                        for (int j = 0; j < parsedInner.Usings.Count; j++)
                        {
                            string usingLine = parsedInner.Usings[j];
                            if (usingSet.Add(usingLine))
                            {
                                orderedUsings.Add(usingLine);
                            }
                        }

                        string innerBody = DedentLines(NormalizeMergedBody(parsedInner.Body));
                        if (!string.IsNullOrWhiteSpace(innerBody))
                        {
                            bodies.Add(innerBody);
                        }
                    }
                    else
                    {
                        canWrapSingleNamespace = false;
                        bodies.Add(NormalizeMergedBody(sourceBody));
                    }
                }
            }

            if (bodies.Count == 0)
            {
                return;
            }

            string runtimePath = Path.Combine(outputPath, "Runtime.cs");
            StringBuilder builder = new();
            for (int i = 0; i < orderedUsings.Count; i++)
            {
                builder.AppendLine(orderedUsings[i]);
            }
            if (orderedUsings.Count > 0)
            {
                builder.AppendLine();
            }

            string combinedBody;
            if (canWrapSingleNamespace)
            {
                StringBuilder nsBuilder = new();
                nsBuilder.AppendLine($"namespace {runtimeNamespace}");
                nsBuilder.AppendLine("{");

                for (int i = 0; i < bodies.Count; i++)
                {
                    nsBuilder.AppendLine(IndentLines(bodies[i], "    "));
                    if (i + 1 < bodies.Count)
                    {
                        nsBuilder.AppendLine();
                    }
                }

                nsBuilder.AppendLine("}");
                combinedBody = nsBuilder.ToString().TrimEnd();
            }
            else
            {
                combinedBody = string.Join($"{Environment.NewLine}{Environment.NewLine}", bodies).TrimEnd();
            }

            combinedBody = WrapRuntimeBodyWithGuard(combinedBody);
            builder.AppendLine(combinedBody);
            string runtimeText = builder.ToString().TrimEnd() + Environment.NewLine;
            runtimeText = NormalizeLeadingTabsPerLine(runtimeText, 4);
            File.WriteAllText(runtimePath, runtimeText);
            LogInfo($"Generated runtime file: {runtimePath}");
        }

        private static string WrapRuntimeBodyWithGuard(string body)
        {
            return $"#if !{RuntimeSourceExcludeSymbol}{Environment.NewLine}{body}{Environment.NewLine}#endif";
        }

        private string GetRuntimeNamespace()
        {
            return string.IsNullOrWhiteSpace(config.RuntimeNamespace)
                ? "BGCS.Runtime"
                : config.RuntimeNamespace;
        }

        private string RewriteRuntimeNamespace(string text)
        {
            string runtimeNamespace = GetRuntimeNamespace();
            text = text.Replace("namespace BGCS.Runtime;", $"namespace {runtimeNamespace};", StringComparison.Ordinal);
            text = text.Replace("namespace BGCS.Runtime\r\n", $"namespace {runtimeNamespace}\r\n", StringComparison.Ordinal);
            text = text.Replace("namespace BGCS.Runtime\n", $"namespace {runtimeNamespace}\n", StringComparison.Ordinal);
            return text;
        }

        private static IReadOnlyList<(string Name, string Content)> GetEmbeddedRuntimeSources()
        {
            const string prefix = "BGCS.RuntimeSources.";
            var assembly = typeof(CsCodeGenerator).Assembly;
            string[] resources = assembly
                .GetManifestResourceNames()
                .Where(x => x.StartsWith(prefix, StringComparison.Ordinal) && x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (resources.Length == 0)
            {
                return [];
            }

            List<(string Name, string Content)> sources = new(resources.Length);
            for (int i = 0; i < resources.Length; i++)
            {
                string resource = resources[i];
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream == null)
                {
                    continue;
                }

                using StreamReader reader = new(stream);
                sources.Add((resource, reader.ReadToEnd()));
            }

            return sources;
        }

        private IReadOnlyList<(string Name, string Content)> GetRuntimeSourcesFromFileSystem()
        {
            string? runtimeDirectory = ResolveRuntimeSourceDirectory();
            if (runtimeDirectory == null)
            {
                return [];
            }

            string[] runtimeFiles = Directory
                .GetFiles(runtimeDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            List<(string Name, string Content)> sources = new(runtimeFiles.Length);
            for (int i = 0; i < runtimeFiles.Length; i++)
            {
                string path = runtimeFiles[i];
                sources.Add((Path.GetFileName(path), File.ReadAllText(path)));
            }

            return sources;
        }

        private static string? ResolveRuntimeSourceDirectory()
        {
            HashSet<string> roots = [];
            roots.Add(Directory.GetCurrentDirectory());
            roots.Add(AppContext.BaseDirectory);

            foreach (string root in roots)
            {
                string? dir = root;
                while (!string.IsNullOrWhiteSpace(dir))
                {
                    string candidate = Path.Combine(dir, "src", "BGCS.Runtime");
                    if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "NativeNameAttribute.cs")))
                    {
                        return candidate;
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }

            return null;
        }

        private static string NormalizeRuntimeTextForMerge(string text)
        {
            string normalized = text.Replace("\r\n", "\n").TrimStart('\uFEFF');
            string[] lines = normalized.Split('\n');
            if (TryFindFileScopedNamespace(lines, out int namespaceLineIndex, out string? namespaceName))
            {
                string prefix = string.Join(Environment.NewLine, lines[..namespaceLineIndex]).TrimEnd();
                string body = string.Join(Environment.NewLine, lines[(namespaceLineIndex + 1)..]);
                string indentedBody = IndentLines(body, "    ");

                StringBuilder builder = new();
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    builder.AppendLine(prefix);
                }
                builder.AppendLine($"namespace {namespaceName}");
                builder.AppendLine("{");
                builder.AppendLine(indentedBody);
                builder.Append('}');
                return builder.ToString();
            }

            return text;
        }

        private static bool TryFindFileScopedNamespace(string[] lines, out int namespaceLineIndex, out string? namespaceName)
        {
            namespaceLineIndex = -1;
            namespaceName = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (TryParseFileScopedNamespace(trimmed, out namespaceName))
                {
                    namespaceLineIndex = i;
                    return true;
                }

                if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                return false;
            }

            return false;
        }

        private static bool TryParseFileScopedNamespace(string line, out string? namespaceName)
        {
            namespaceName = null;
            const string prefix = "namespace ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(';'))
            {
                return false;
            }

            string candidate = line[prefix.Length..^1].Trim();
            if (candidate.Length == 0)
            {
                return false;
            }

            namespaceName = candidate;
            return true;
        }

        private static string IndentLines(string text, string indent)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                {
                    continue;
                }

                lines[i] = indent + lines[i];
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string NormalizeMergedBody(string text)
        {
            string normalized = text.Replace("\r\n", "\n").Trim('\r', '\n');
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            string[] lines = normalized.Split('\n');
            List<string> compact = new(lines.Length);
            bool previousBlank = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                bool blank = string.IsNullOrWhiteSpace(line);
                if (blank)
                {
                    if (previousBlank)
                    {
                        continue;
                    }

                    previousBlank = true;
                    compact.Add(string.Empty);
                    continue;
                }

                previousBlank = false;
                compact.Add(line);
            }

            while (compact.Count > 0 && compact[0].Length == 0)
            {
                compact.RemoveAt(0);
            }

            while (compact.Count > 0 && compact[^1].Length == 0)
            {
                compact.RemoveAt(compact.Count - 1);
            }

            return string.Join(Environment.NewLine, compact);
        }

        private static string DedentLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = ExpandLeadingTabs(lines[i], 4);
            }

            int? minIndent = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int indent = 0;
                while (indent < line.Length && char.IsWhiteSpace(line[indent]))
                {
                    indent++;
                }

                minIndent = minIndent is null ? indent : Math.Min(minIndent.Value, indent);
            }

            if (minIndent is null || minIndent.Value == 0)
            {
                return string.Join(Environment.NewLine, lines);
            }

            int remove = minIndent.Value;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                lines[i] = line.Length >= remove ? line[remove..] : string.Empty;
            }

            return string.Join(Environment.NewLine, AlignOpeningBracesWithPreviousLine(lines));
        }

        private static string ExpandLeadingTabs(string line, int tabSize)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }

            int i = 0;
            while (i < line.Length && (line[i] == '\t' || line[i] == ' '))
            {
                i++;
            }

            if (i == 0)
            {
                return line;
            }

            string prefix = line[..i];
            if (!prefix.Contains('\t'))
            {
                return line;
            }

            string expanded = prefix.Replace("\t", new string(' ', tabSize), StringComparison.Ordinal);
            return expanded + line[i..];
        }

        private static string NormalizeLeadingTabsPerLine(string text, int tabSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string normalized = text.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = ExpandLeadingTabs(lines[i], tabSize);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string[] AlignOpeningBracesWithPreviousLine(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "{")
                {
                    continue;
                }

                int prev = i - 1;
                while (prev >= 0 && string.IsNullOrWhiteSpace(lines[prev]))
                {
                    prev--;
                }

                if (prev < 0)
                {
                    continue;
                }

                string prevLine = lines[prev];
                string braceLine = lines[i];
                int prevIndent = GetLeadingWhitespaceCount(prevLine);
                int braceIndent = GetLeadingWhitespaceCount(braceLine);
                if (braceIndent > prevIndent)
                {
                    lines[i] = new string(' ', prevIndent) + "{";
                }
            }

            return lines;
        }

        private static int GetLeadingWhitespaceCount(string line)
        {
            int count = 0;
            while (count < line.Length && char.IsWhiteSpace(line[count]))
            {
                count++;
            }

            return count;
        }

        private static bool TryUnwrapNamespaceBody(string body, string namespaceName, out string unwrappedBody)
        {
            unwrappedBody = string.Empty;
            string trimmed = body.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            string fileScopedPrefix = $"namespace {namespaceName};";
            if (trimmed.StartsWith(fileScopedPrefix, StringComparison.Ordinal))
            {
                unwrappedBody = TrimBoundaryNewLines(trimmed[fileScopedPrefix.Length..]);
                return true;
            }

            string blockScopedPrefix = $"namespace {namespaceName}";
            if (!trimmed.StartsWith(blockScopedPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            int namespaceDeclarationEnd = blockScopedPrefix.Length;
            while (namespaceDeclarationEnd < trimmed.Length && char.IsWhiteSpace(trimmed[namespaceDeclarationEnd]))
            {
                namespaceDeclarationEnd++;
            }

            if (namespaceDeclarationEnd >= trimmed.Length || trimmed[namespaceDeclarationEnd] != '{')
            {
                return false;
            }

            int bodyStart = namespaceDeclarationEnd + 1;
            int depth = 1;
            int index = bodyStart;
            for (; index < trimmed.Length; index++)
            {
                char c = trimmed[index];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            if (depth != 0)
            {
                return false;
            }

            string suffix = trimmed[(index + 1)..].Trim();
            if (suffix.Length != 0)
            {
                return false;
            }

            unwrappedBody = TrimBoundaryNewLines(trimmed[bodyStart..index]);
            return true;
        }

        private static string TrimBoundaryNewLines(string text)
        {
            return text.Trim('\r', '\n');
        }

        private static string ExtractLeadingUsingLines(string body, List<string> orderedUsings, HashSet<string> usingSet)
        {
            string normalized = body.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');

            int index = 0;
            while (index < lines.Length)
            {
                string line = lines[index].Trim();
                if (line.Length == 0)
                {
                    index++;
                    continue;
                }

                if (line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(';'))
                {
                    if (usingSet.Add(line))
                    {
                        orderedUsings.Add(line);
                    }
                    index++;
                    continue;
                }

                break;
            }

            return TrimBoundaryNewLines(string.Join(Environment.NewLine, lines.Skip(index)));
        }

        private static ParsedMergedFile ParseMergedFile(string text)
        {
            string normalized = text.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');

            int index = 0;
            string? banner = null;

            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("// ------------------------------------------------------------------------------", StringComparison.Ordinal))
            {
                int autoStart = -1;
                int autoEnd = -1;
                int secondSeparator = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (autoStart == -1 && string.Equals(line, "// <auto-generated>", StringComparison.Ordinal))
                    {
                        autoStart = i;
                    }
                    if (autoEnd == -1 && string.Equals(line, "// </auto-generated>", StringComparison.Ordinal))
                    {
                        autoEnd = i;
                    }
                    if (i > 0 && string.Equals(line, "// ------------------------------------------------------------------------------", StringComparison.Ordinal))
                    {
                        secondSeparator = i;
                        break;
                    }
                }

                if (autoStart != -1 && autoEnd != -1 && secondSeparator != -1 && autoStart < autoEnd)
                {
                    banner = string.Join(Environment.NewLine, lines.Take(secondSeparator + 1)).TrimEnd();
                    index = secondSeparator + 1;
                    while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                    {
                        index++;
                    }
                }
            }

            List<string> usings = [];
            while (index < lines.Length)
            {
                string line = lines[index].Trim();
                if (line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(';'))
                {
                    usings.Add(line);
                    index++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    index++;
                    continue;
                }

                break;
            }

            string body = TrimBoundaryNewLines(string.Join(Environment.NewLine, lines.Skip(index)));
            return new ParsedMergedFile(banner, usings, body);
        }

        private sealed record ParsedMergedFile(string? HeaderBanner, List<string> Usings, string Body);

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
