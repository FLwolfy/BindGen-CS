using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BGCS.Core.Execution;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Executes a native build plan directly through the operating-system process API.
/// </summary>
public static class NativeBuildExecutor
{
    /// <summary>
    /// Runs one frozen compiler invocation and stops its process tree when the deadline expires.
    /// </summary>
    /// <param name="plan">Frozen compiler inputs.</param>
    /// <param name="timeout">Positive maximum process duration.</param>
    /// <returns>Captured process output, exit status and expected artifact path.</returns>
    /// <exception cref="ArgumentNullException">The plan is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive.</exception>
    public static NativeBuildResult Execute(
        NativeBuildPlan plan,
        TimeSpan timeout
    ) {
        ArgumentNullException.ThrowIfNull(plan);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Build timeout must be positive.");
        string? outputDirectory = Path.GetDirectoryName(plan.outputFile);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        ProcessExecutionResult result = ProcessExecutor.ExecuteAsync(
            plan.executable, plan.arguments, plan.workingDirectory, timeout).GetAwaiter().GetResult();
        return new(result.exitCode, result.standardOutput, result.standardError, result.timedOut, plan.outputFile);
    }

    /// <summary>
    /// Materializes deterministic provider inputs and executes each pipeline step without a command shell.
    /// </summary>
    /// <param name="pipeline">Frozen input files and ordered compiler steps.</param>
    /// <param name="timeout">Positive maximum duration shared by all process steps.</param>
    /// <returns>Executed step results; later steps are omitted after failure or timeout.</returns>
    /// <exception cref="ArgumentNullException">The pipeline is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is not positive.</exception>
    /// <exception cref="InvalidDataException">The pipeline has no execution steps.</exception>
    public static NativeBuildPipelineResult Execute(
        NativeBuildPipeline pipeline,
        TimeSpan timeout
    ) {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Build timeout must be positive.");
        if (pipeline.steps.Count == 0)
            throw new InvalidDataException("A native build pipeline must contain at least one step.");
        foreach (NativeBuildInputFile input in pipeline.inputFiles)
        {
            string fullPath = Path.GetFullPath(input.path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, input.content);
        }

        Stopwatch elapsed = Stopwatch.StartNew();
        List<NativeBuildStepResult> results = [];
        foreach (NativeBuildStep step in pipeline.steps)
        {
            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                results.Add(new(step.name, -1, string.Empty, "Pipeline timeout expired before this step started.", true));
                break;
            }

            NativeBuildResult result = Execute(new NativeBuildPlan(pipeline.provider, step.executable, step.arguments, step.workingDirectory, pipeline.outputFile), remaining);
            results.Add(new(step.name, result.exitCode, result.standardOutput, result.standardError, result.timedOut));
            if (result.exitCode != 0 || result.timedOut)
                break;
        }

        return new(pipeline.provider, results.AsReadOnly(), results.Any(result => result.timedOut), pipeline.outputFile);
    }
}
