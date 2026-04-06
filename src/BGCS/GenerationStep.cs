namespace BGCS
{
    using BGCS.Core;
    using BGCS.Metadata;

    /// <summary>
    /// Defines the public class <c>GenerationStep</c> used by the generation pipeline.
    /// </summary>
    public abstract class GenerationStep : LoggerBase
    {
        protected readonly CsCodeGenerator generator;
        protected readonly CsCodeGeneratorConfig config;

        /// <summary>
        /// Initializes a new instance of <see cref="GenerationStep"/>.
        /// </summary>
        public GenerationStep(CsCodeGenerator generator, CsCodeGeneratorConfig config)
        {
            this.generator = generator;
            this.config = config;
            LogEvent += generator.Log;
        }

        /// <summary>
        /// Gets or sets the <c>Name</c> setting used by this component.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Gets or sets the <c>Enabled</c> setting used by this component.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Performs the operation implemented by <c>Configure</c>.
        /// </summary>
        public abstract void Configure(CsCodeGeneratorConfig config);

        /// <summary>
        /// Performs the operation implemented by <c>Generate</c>.
        /// </summary>
        public abstract void Generate(FileSet files, ParseResult result, string outputPath, CsCodeGeneratorConfig config, CsCodeGeneratorMetadata metadata);

        /// <summary>
        /// Performs the operation implemented by <c>CopyToMetadata</c>.
        /// </summary>
        public abstract void CopyToMetadata(CsCodeGeneratorMetadata metadata);

        /// <summary>
        /// Performs the operation implemented by <c>CopyFromMetadata</c>.
        /// </summary>
        public abstract void CopyFromMetadata(CsCodeGeneratorMetadata metadata);

        /// <summary>
        /// Performs the operation implemented by <c>Reset</c>.
        /// </summary>
        public abstract void Reset();

        /// <summary>
        /// Performs the operation implemented by <c>GetGenerationStep</c>.
        /// </summary>
        /// <returns>Result produced by <c>GetGenerationStep</c>.</returns>
        public T GetGenerationStep<T>() where T : GenerationStep
        {
            return generator.GetGenerationStep<T>();
        }
    }
}
