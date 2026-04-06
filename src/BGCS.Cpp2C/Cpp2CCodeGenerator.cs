namespace BGCS.Cpp2C
{
    using BGCS.Core;
    using BGCS.Core.Logging;
    using BGCS.Cpp2C.GenerationSteps;
    using BGCS.Cpp2C.Metadata;
    using BGCS.CppAst.Diagnostics;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.CppAst.Parsing;
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

        /// <summary>
        /// Initializes a new instance of <see cref="Cpp2CCodeGenerator"/>.
        /// </summary>
        public Cpp2CCodeGenerator(Cpp2CGeneratorConfig settings) : base(settings)
        {
        }

        /// <summary>
        /// Exposes public member <c>generationSteps</c>.
        /// </summary>
        public IReadOnlyList<GenerationStep> GenerationSteps => generationSteps;

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
            var options = new CppParserOptions
            {
                ParseMacros = true,
                ParseComments = true,
                ParseSystemIncludes = true,
                ParserKind = CppParserKind.Cpp,
                AutoSquashTypedef = true,
            };

            for (int i = 0; i < config.AdditionalArguments.Count; i++)
            {
                options.AdditionalArguments.Add(config.AdditionalArguments[i]);
            }

            for (int i = 0; i < config.IncludeFolders.Count; i++)
            {
                options.IncludeFolders.Add(config.IncludeFolders[i]);
            }

            for (int i = 0; i < config.SystemIncludeFolders.Count; i++)
            {
                options.SystemIncludeFolders.Add(config.SystemIncludeFolders[i]);
            }

            for (int i = 0; i < config.Defines.Count; i++)
            {
                options.Defines.Add(config.Defines[i]);
            }

            //options.ConfigureForWindowsMsvc(CppTargetCpu.X86_64);
            options.AdditionalArguments.Add("-std=c++23");

            return options;
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

            var compilation = CppParser.ParseFiles(headerFiles, options);

            Generate(compilation, headerFiles, outputPath, allowedHeaders);
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        public virtual void Generate(CppCompilation compilation, List<string> headerFiles, string outputPath, List<string>? allowedHeaders)
        {
            EnsureGenerationPipeline();
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(Path.Combine(outputPath, "include"));
            Directory.CreateDirectory(Path.Combine(outputPath, "src"));
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
                return;
            }

            allowedHeaders ??= [];
            allowedHeaders.AddRange(headerFiles);

            FileSet files = new(allowedHeaders.Select(PathHelper.GetPath));

            foreach (var meta in copyFromPending)
            {
                foreach (var step in generationSteps)
                {
                    step.CopyFromMetadata(meta);
                }
            }

            LogInfo($"Configuring Steps...");
            foreach (var step in generationSteps)
            {
                step.Configure(config);
            }

            ParseResult result = new(compilation);
            foreach (var step in generationSteps)
            {
                if (step.Enabled)
                {
                    LogInfo($"Generating {step.Name}...");
                    step.Generate(files, result, outputPath, config, metadata);
                    step.CopyToMetadata(metadata);
                }
            }
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
