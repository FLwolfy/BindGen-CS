using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Captures executed steps and final artifact status for a pipeline.
/// </summary>
/// <param name="provider">Provider identifying the executed pipeline.</param>
/// <param name="steps">Completed or timed-out steps copied into a read-only snapshot.</param>
/// <param name="timedOut">Whether execution exhausted the shared pipeline deadline.</param>
/// <param name="outputFile">Expected final artifact path.</param>
public sealed record NativeBuildPipelineResult(
    string provider,
    IReadOnlyList<NativeBuildStepResult> steps,
    bool timedOut,
    string outputFile
)
{
    /// <summary>
    /// Completed or timed-out steps copied into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<NativeBuildStepResult> steps { get; } = Array.AsReadOnly((steps ?? throw new ArgumentNullException(nameof(steps))).ToArray());

    /// <summary>
    /// Gets whether every executed step succeeded and the final artifact exists.
    /// </summary>
    public bool success => !this.timedOut && this.steps.Count > 0 && this.steps.All(step => !step.timedOut && step.exitCode == 0) && File.Exists(this.outputFile);
}
