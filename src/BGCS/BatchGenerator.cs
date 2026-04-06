namespace BGCS
{
    using CommandLine;
    using BGCS.CppAst.Parsing;
    using BGCS.Metadata;
    using BGCS.Patching;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>BatchGenerator</c> used by the generation pipeline.
    /// </summary>
    public class BatchGenerator
    {
        private CsCodeGeneratorConfig config = null!;
        private CsCodeGenerator generator = null!;
        private CLIGeneratorOptions? options;
        private readonly List<IPrePatch> prePatches = [];
        private readonly List<IPostPatch> postPatches = [];
        private DateTime start;

        /// <summary>
        /// Performs the operation implemented by <c>Create</c>.
        /// </summary>
        /// <returns>Result produced by <c>Create</c>.</returns>
        public static BatchGenerator Create()
        {
            BatchGenerator generator = new();
            generator.Start();
            return generator;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Start</c>.
        /// </summary>
        /// <returns>Result produced by <c>Start</c>.</returns>
        public BatchGenerator Start()
        {
            start = DateTime.Now;
            return this;
        }

        /// <summary>
        /// Loads configuration and creates a generator instance of <typeparamref name="T"/>.
        /// </summary>
        /// <returns>The current <see cref="BatchGenerator"/> for fluent chaining.</returns>
        public BatchGenerator Setup<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string configPath) where T : CsCodeGenerator
        {
            config = CsCodeGeneratorConfig.Load(configPath);
            var type = typeof(T);
            var ctor = type.GetConstructor([typeof(CsCodeGeneratorConfig)]);
            generator = (T)ctor!.Invoke([config]);
            generator.LogToConsole();
            generator.CLIOptions = options;
            foreach (var patch in prePatches)
            {
                generator.PatchEngine.RegisterPrePatch(patch);
            }
            foreach (var patch in postPatches)
            {
                generator.PatchEngine.RegisterPostPatch(patch);
            }
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithArgs</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithArgs</c>.</returns>
        public BatchGenerator WithArgs(string[] args)
        {
            options = Parser.Default.ParseArguments<CLIGeneratorOptions>(args).Value;
            if (generator != null)
            {
                generator.CLIOptions = options;
            }

            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AddGlobalPrePatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>AddGlobalPrePatch</c>.</returns>
        public BatchGenerator AddGlobalPrePatch(IPrePatch patch)
        {
            prePatches.Add(patch);
            generator?.PatchEngine.RegisterPrePatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AddGlobalPostPatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>AddGlobalPostPatch</c>.</returns>
        public BatchGenerator AddGlobalPostPatch(IPostPatch patch)
        {
            postPatches.Add(patch);
            generator?.PatchEngine.RegisterPostPatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AddPrePatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>AddPrePatch</c>.</returns>
        public BatchGenerator AddPrePatch(IPrePatch patch)
        {
            generator.PatchEngine.RegisterPrePatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AddPostPatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>AddPostPatch</c>.</returns>
        public BatchGenerator AddPostPatch(IPostPatch patch)
        {
            generator.PatchEngine.RegisterPostPatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public BatchGenerator Generate(List<string> sources, string output, List<string>? allowedHeaders = null)
        {
            generator.Generate(sources, output, allowedHeaders);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public BatchGenerator Generate(string source, string output, List<string>? allowedHeaders = null)
        {
            generator.Generate(source, output, allowedHeaders);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public BatchGenerator Generate(CppParserOptions parserOptions, List<string> sources, string output, List<string>? allowedHeaders = null)
        {
            generator.Generate(parserOptions, sources, output, allowedHeaders);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        /// <returns>Result produced by <c>Generate</c>.</returns>
        public BatchGenerator Generate(CppParserOptions parserOptions, string source, string output, List<string>? allowedHeaders = null)
        {
            generator.Generate(parserOptions, source, output, allowedHeaders);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetMetadata</c>.</returns>
        public BatchGenerator GetMetadata(out CsCodeGeneratorMetadata metadata)
        {
            metadata = generator.GetMetadata();
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetConfig</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetConfig</c>.</returns>
        public BatchGenerator GetConfig(out CsCodeGeneratorConfig config)
        {
            config = this.config;
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AlterGenerator</c>.
        /// </summary>
        /// <returns>Result produced by <c>AlterGenerator</c>.</returns>
        public BatchGenerator AlterGenerator(Action<CsCodeGenerator> action)
        {
            action(generator);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AlterConfig</c>.
        /// </summary>
        /// <returns>Result produced by <c>AlterConfig</c>.</returns>
        public BatchGenerator AlterConfig(Action<CsCodeGeneratorConfig> action)
        {
            action(config);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>MergeConfig</c>.
        /// </summary>
        /// <returns>Result produced by <c>MergeConfig</c>.</returns>
        public BatchGenerator MergeConfig(CsCodeGeneratorConfig baseConfig, MergeOptions options)
        {
            config.Merge(baseConfig, options);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>CopyFromMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>CopyFromMetadata</c>.</returns>
        public BatchGenerator CopyFromMetadata(CsCodeGeneratorMetadata metadata)
        {
            generator.CopyFrom(metadata);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>Finish</c>.
        /// </summary>
        public void Finish()
        {
            var end = DateTime.Now;
            var elapsed = end - start;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"All Done! Generation took {elapsed.TotalSeconds:n2}s");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
