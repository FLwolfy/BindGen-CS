namespace BGCS
{
    using CommandLine;
    using BGCS.Metadata;
    using BGCS.Patching;
    using Microsoft.Extensions.Options;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public struct <c>MacroBuilder</c> used by the generation pipeline.
    /// </summary>
    public readonly ref struct MacroBuilder
    {
        private readonly List<string> defines;
        private readonly string? prefix;

        /// <summary>
        /// Initializes a new instance of <see cref="MacroBuilder"/>.
        /// </summary>
        public MacroBuilder(string? prefix = null)
        {
            defines = [];
            this.prefix = prefix;
        }

        /// <summary>
        /// Performs the operation implemented by <c>MacroBuilder</c>.
        /// </summary>
        /// <returns>Result produced by <c>MacroBuilder</c>.</returns>
        public MacroBuilder(List<string> defines, string? prefix = null)
        {
            this.defines = defines;
            this.prefix = prefix;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AddMacro</c>.
        /// </summary>
        /// <returns>Result produced by <c>AddMacro</c>.</returns>
        public readonly MacroBuilder AddMacro(string value)
        {
            defines.Add(prefix + value);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithPrefix</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithPrefix</c>.</returns>
        public readonly MacroBuilder WithPrefix(string prefix)
        {
            return new(defines, prefix);
        }

        /// <summary>
        /// Delegate callback definition for <c>callback</c>.
        /// </summary>
        public delegate string? SelectorFormat<T>(T option, string? value, bool condition);

        /// <summary>
        /// Performs the operation implemented by <c>Selector</c>.
        /// </summary>
        /// <returns>Result produced by <c>Selector</c>.</returns>
        public readonly Selector<T> WithSelector<T>(T current, SelectorFormat<T> format, Func<T, T, bool> selector)
        {
            return new(this, current, format, selector);
        }

        /// <summary>
        /// Performs the operation implemented by <c>Selector</c>.
        /// </summary>
        /// <returns>Result produced by <c>Selector</c>.</returns>
        public readonly Selector<T> WithSelector<T>(T current, SelectorFormat<T> format)
        {
            return new(this, current, format, EqualityComparer<T>.Default.Equals);
        }

        /// <summary>
        /// Defines the public struct <c>Selector</c> used by the generation pipeline.
        /// </summary>
        public readonly ref struct Selector<T>
        {
            private readonly MacroBuilder builder;
            private readonly T current;
            private readonly SelectorFormat<T> format;
            private readonly Func<T, T, bool> selector;

            /// <summary>
            /// Initializes a new instance of <see cref="Selector"/>.
            /// </summary>
            public Selector(MacroBuilder builder, T current, SelectorFormat<T> format, Func<T, T, bool> selector)
            {
                this.builder = builder;
                this.current = current;
                this.format = format;
                this.selector = selector;
            }

            private readonly void AddMacro(T conditional, bool condition, string? value)
            {
                string? macro = format(conditional, value, condition);
                if (macro == null) return;
                builder.AddMacro(macro);
            }

            /// <summary>
            /// Performs the operation implemented by <c>Option</c>.
            /// </summary>
            /// <returns>Result produced by <c>Option</c>.</returns>
            public readonly Selector<T> Option(T conditional, string? value = null)
            {
                AddMacro(conditional, selector(current, conditional), value);
                return this;
            }

            /// <summary>
            /// Performs the operation implemented by <c>Option</c>.
            /// </summary>
            /// <returns>Result produced by <c>Option</c>.</returns>
            public readonly Selector<T> Option(T conditional, ReadOnlySpan<string> values)
            {
                bool condition = selector(current, conditional);
                foreach (var value in values)
                {
                    AddMacro(conditional, condition, value);
                }
                return this;
            }
        }
    }

    /// <summary>
    /// Defines the public class <c>GeneratorBuilder</c> used by the generation pipeline.
    /// </summary>
    public class GeneratorBuilder
    {
        private CsCodeGeneratorConfig config = null!;
        private CsCodeGenerator generator = null!;
        private CLIGeneratorOptions? options;
        private readonly List<IPrePatch> prePatches = [];
        private readonly List<IPostPatch> postPatches = [];
        private readonly List<GenEventHandler<CsCodeGenerator, CsCodeGeneratorConfig>> onPostConfigureCallbacks = [];

        /// <summary>
        /// Performs the operation implemented by <c>Create</c>.
        /// </summary>
        /// <returns>Result produced by <c>Create</c>.</returns>
        public static GeneratorBuilder Create()
        {
            GeneratorBuilder generator = new();
            return generator;
        }

        /// <summary>
        /// Creates a generator builder from a configuration file and concrete generator type.
        /// </summary>
        /// <returns>A configured <see cref="GeneratorBuilder"/> instance.</returns>
        public static GeneratorBuilder Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string configPath) where T : CsCodeGenerator
        {
            GeneratorBuilder generator = new();
            generator.Setup<T>(configPath);
            return generator;
        }

        /// <summary>
        /// Creates a generator builder from an existing configuration and concrete generator type.
        /// </summary>
        /// <returns>A configured <see cref="GeneratorBuilder"/> instance.</returns>
        public static GeneratorBuilder Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(CsCodeGeneratorConfig config) where T : CsCodeGenerator
        {
            GeneratorBuilder generator = new();
            generator.Setup<T>(config);
            return generator;
        }

        /// <summary>
        /// Initializes this builder with configuration loaded from <paramref name="configPath"/> and generator type <typeparamref name="T"/>.
        /// </summary>
        /// <returns>The current <see cref="GeneratorBuilder"/> for fluent chaining.</returns>
        public GeneratorBuilder Setup<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string configPath) where T : CsCodeGenerator
        {
            return Setup<T>(CsCodeGeneratorConfig.Load(configPath));
        }

        /// <summary>
        /// Initializes this builder from an existing configuration and generator type <typeparamref name="T"/>.
        /// </summary>
        /// <returns>The current <see cref="GeneratorBuilder"/> for fluent chaining.</returns>
        public GeneratorBuilder Setup<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(CsCodeGeneratorConfig config) where T : CsCodeGenerator
        {
            if (generator != null)
            {
                onPostConfigureCallbacks.Clear();
                generator.PostConfigure -= OnPostConfigure;
            }
            this.config = config;
            var type = typeof(T);
            var ctor = type.GetConstructor([typeof(CsCodeGeneratorConfig)]);
            generator = (T)ctor!.Invoke([config]);
            generator.LogToConsole();
            foreach (var patch in prePatches)
            {
                generator.PatchEngine.RegisterPrePatch(patch);
            }
            foreach (var patch in postPatches)
            {
                generator.PatchEngine.RegisterPostPatch(patch);
            }

            generator.PostConfigure += OnPostConfigure;
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithArgs</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithArgs</c>.</returns>
        public GeneratorBuilder WithArgs(string[] args)
        {
            options = Parser.Default.ParseArguments<CLIGeneratorOptions>(args).Value;
            if (generator != null)
            {
                generator.CLIOptions = options;
            }

            return this;
        }

        private void OnPostConfigure(CsCodeGenerator sender, CsCodeGeneratorConfig args)
        {
            foreach (var callback in onPostConfigureCallbacks)
            {
                callback(sender, args);
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithGlobalPrePatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithGlobalPrePatch</c>.</returns>
        public GeneratorBuilder WithGlobalPrePatch(IPrePatch patch)
        {
            prePatches.Add(patch);
            generator?.PatchEngine.RegisterPrePatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithGlobalPostPatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithGlobalPostPatch</c>.</returns>
        public GeneratorBuilder WithGlobalPostPatch(IPostPatch patch)
        {
            postPatches.Add(patch);
            generator?.PatchEngine.RegisterPostPatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithPrePatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithPrePatch</c>.</returns>
        public GeneratorBuilder WithPrePatch(IPrePatch patch)
        {
            generator.PatchEngine.RegisterPrePatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithPostPatch</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithPostPatch</c>.</returns>
        public GeneratorBuilder WithPostPatch(IPostPatch patch)
        {
            generator.PatchEngine.RegisterPostPatch(patch);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>OnPostConfigure</c>.
        /// </summary>
        /// <returns>Result produced by <c>OnPostConfigure</c>.</returns>
        public GeneratorBuilder OnPostConfigure(GenEventHandler<CsCodeGenerator, CsCodeGeneratorConfig> onPostConfigure)
        {
            onPostConfigureCallbacks.Add(onPostConfigure);
            return this;
        }

        /// <summary>
        /// Generates code from the specified source files.
        /// </summary>
        /// (Optional) A list of allowed header files. <b>Note:</b> This parameter is not yet supported by BGCS.Legacy builds and will be ignored.
        /// </param>
        /// <returns>The current <see cref="GeneratorBuilder"/> instance.</returns>
        public GeneratorBuilder Generate(List<string> sources, string output, List<string>? allowedHeaders = null)
        {
            generator.Generate(sources, output, allowedHeaders);
            return this;
        }

        /// <summary>
        /// Generates code from the specified source file.
        /// </summary>
        /// (Optional) A list of allowed header files. <b>Note:</b> This parameter is not yet supported by BGCS.Legacy builds and will be ignored.
        /// </param>
        /// <returns>The current <see cref="GeneratorBuilder"/> instance.</returns>
        public GeneratorBuilder Generate(string source, string output, List<string>? allowedHeaders = null)
        {
            generator.Generate(source, output, allowedHeaders);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetMetadata</c>.</returns>
        public GeneratorBuilder GetMetadata(out CsCodeGeneratorMetadata metadata)
        {
            metadata = generator.GetMetadata();
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>GetConfig</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetConfig</c>.</returns>
        public GeneratorBuilder GetConfig(out CsCodeGeneratorConfig config)
        {
            config = this.config;
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AlterGenerator</c>.
        /// </summary>
        /// <returns>Result produced by <c>AlterGenerator</c>.</returns>
        public GeneratorBuilder AlterGenerator(Action<CsCodeGenerator> action)
        {
            action(generator);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>AlterConfig</c>.
        /// </summary>
        /// <returns>Result produced by <c>AlterConfig</c>.</returns>
        public GeneratorBuilder AlterConfig(Action<CsCodeGeneratorConfig> action)
        {
            action(config);
            return this;
        }

        /// <summary>
        /// Delegate callback definition for <c>MacroBuilderCallback</c>.
        /// </summary>
        public delegate void MacroBuilderCallback(MacroBuilder builder);

        /// <summary>
        /// Performs the operation implemented by <c>WithMacros</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithMacros</c>.</returns>
        public GeneratorBuilder WithMacros(MacroBuilderCallback action) => WithMacros(null, action);

        /// <summary>
        /// Performs the operation implemented by <c>WithMacros</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithMacros</c>.</returns>
        public GeneratorBuilder WithMacros(string? prefix, MacroBuilderCallback action)
        {
            action(new MacroBuilder(config.Defines, prefix));
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>MergeConfig</c>.
        /// </summary>
        /// <returns>Result produced by <c>MergeConfig</c>.</returns>
        public GeneratorBuilder MergeConfig(CsCodeGeneratorConfig baseConfig, MergeOptions options)
        {
            config.Merge(baseConfig, options);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>CopyFromMetadata</c>.
        /// </summary>
        /// <returns>Result produced by <c>CopyFromMetadata</c>.</returns>
        public GeneratorBuilder CopyFromMetadata(CsCodeGeneratorMetadata? metadata)
        {
            if (metadata == null) return this;
            generator.CopyFrom(metadata);
            return this;
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithFunctionTableEntires</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithFunctionTableEntires</c>.</returns>
        public GeneratorBuilder WithFunctionTableEntires(CsCodeGeneratorMetadata? metadata)
        {
            if (metadata == null) return this;
            return WithFunctionTableEntires(metadata.FunctionTable.Entries);
        }

        /// <summary>
        /// Performs the operation implemented by <c>WithFunctionTableEntires</c>.
        /// </summary>
        /// <returns>Result produced by <c>WithFunctionTableEntires</c>.</returns>
        public GeneratorBuilder WithFunctionTableEntires(List<CsFunctionTableEntry> entries)
        {
            config.FunctionTableEntries = entries;
            return this;
        }
    }
}
