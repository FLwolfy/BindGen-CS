namespace BGCS
{
    using BGCS.Core;
    using BGCS.CppAst.Model.Metadata;
    using BGCS.Metadata;

    /// <summary>
    /// Defines the public class <c>PreProcessStep</c>.
    /// </summary>
    public abstract class PreProcessStep : LoggerBase
    {
        protected readonly CsCodeGenerator generator;
        protected readonly CsCodeGeneratorConfig config;

        /// <summary>
        /// Initializes a new instance of <see cref="PreProcessStep"/>.
        /// </summary>
        public PreProcessStep(CsCodeGenerator generator, CsCodeGeneratorConfig config)
        {
            this.generator = generator;
            this.config = config;
            LogEvent += generator.Log;
        }

        /// <summary>
        /// Executes public operation <c>Configure</c>.
        /// </summary>
        public abstract void Configure(CsCodeGeneratorConfig config);

        /// <summary>
        /// Executes public operation <c>PreProcess</c>.
        /// </summary>
        public abstract void PreProcess(FileSet files, CppCompilation compilation, CsCodeGeneratorConfig config, CsCodeGeneratorMetadata metadata, ParseResult result);
    }
}
