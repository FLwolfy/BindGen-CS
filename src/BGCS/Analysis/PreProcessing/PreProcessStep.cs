using System;
using BGCS.Configuration;
using BGCS.Core.IO;
using BGCS.Core.Logging;
using BGCS.Facade;

namespace BGCS.Analysis.PreProcessing
{
    using BGCS.CppAst.Model.Metadata;
    using BGCS.Metadata;

    /// <summary>
    /// Contributes attempt-local analysis projections before freezing the binding module; source files remain caller-owned.
    /// </summary>
    public abstract class PreProcessStep : LoggerBase
    {
        /// <summary>
        /// Generator receiving diagnostics and metadata from this preprocessing step.
        /// </summary>
        protected readonly CsCodeGenerator generator;
        /// <summary>
        /// Generation policy borrowed from the owning generator.
        /// </summary>
        protected readonly CsCodeGeneratorConfig config;
        /// <summary>
        /// Retains the generation owner and forwards preprocessing diagnostics to its log.
        /// </summary>
        /// <param name="generator">
        /// The owner retained for diagnostic forwarding.
        /// </param>
        /// <param name="config">
        /// The mutable generation policy borrowed for this step's lifetime.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The generator or configuration is null.
        /// </exception>
        public PreProcessStep(
            CsCodeGenerator generator,
            CsCodeGeneratorConfig config
        ) {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            LogEvent += generator.Log;
        }

        /// <summary>
        /// Transforms the current analysis model before pre-patches and IR analysis.
        /// </summary>
        /// <param name="files">
        /// The selected declaration source set; excluded sources must not contribute output.
        /// </param>
        /// <param name="compilation">
        /// The borrowed native compilation owned by the generation attempt.
        /// </param>
        /// <param name="config">
        /// The mutable policy for this attempt.
        /// </param>
        /// <param name="metadata">
        /// The attempt-owned mutable generated metadata.
        /// </param>
        /// <param name="result">
        /// The attempt-local model receiving analysis contributions.
        /// </param>
        public abstract void PreProcess(
            FileSet files,
            CppCompilation compilation,
            CsCodeGeneratorConfig config,
            CsCodeGeneratorMetadata metadata,
            ParseResult result
        );
    }
}
