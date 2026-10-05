using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Freezes inputs and ordered process steps for one native artifact.
/// </summary>
/// <param name="provider">Provider that produced this pipeline.</param>
/// <param name="inputFiles">Input files copied into a read-only snapshot and materialized before execution.</param>
/// <param name="steps">Ordered process steps copied into a read-only snapshot.</param>
/// <param name="outputFile">Expected final native artifact path.</param>
public sealed record NativeBuildPipeline(
    string provider,
    IReadOnlyList<NativeBuildInputFile> inputFiles,
    IReadOnlyList<NativeBuildStep> steps,
    string outputFile
)
{
    /// <summary>
    /// Input files copied into a read-only snapshot and materialized before execution.
    /// </summary>
    public IReadOnlyList<NativeBuildInputFile> inputFiles { get; } = Array.AsReadOnly((inputFiles ?? throw new ArgumentNullException(nameof(inputFiles))).ToArray());

    /// <summary>
    /// Ordered process steps copied into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<NativeBuildStep> steps { get; } = Array.AsReadOnly((steps ?? throw new ArgumentNullException(nameof(steps))).ToArray());

    /// <summary>
    /// Gets the executable when this pipeline contains exactly one step, or null otherwise.
    /// </summary>
    public string? executable => this.steps.Count == 1 ? this.steps[0].executable : null;

    /// <summary>
    /// Gets the arguments when this pipeline contains exactly one step, or null otherwise.
    /// </summary>
    public IReadOnlyList<string>? arguments => this.steps.Count == 1 ? this.steps[0].arguments : null;

    /// <summary>
    /// Gets the working directory when this pipeline contains exactly one step, or null otherwise.
    /// </summary>
    public string? workingDirectory => this.steps.Count == 1 ? this.steps[0].workingDirectory : null;
}
