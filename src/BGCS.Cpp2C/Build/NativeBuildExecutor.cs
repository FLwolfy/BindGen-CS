using System.Diagnostics;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Executes a native build plan directly through the operating-system process API.
/// </summary>
public static class NativeBuildExecutor
{
    public static NativeBuildResult Execute(NativeBuildPlan plan, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Build timeout must be positive.");

        string? outputDirectory = Path.GetDirectoryName(plan.OutputFile);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        ProcessStartInfo startInfo = new(plan.Executable)
        {
            WorkingDirectory = plan.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in plan.Arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start native compiler '{plan.Executable}'.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        bool completed = process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
        if (!completed)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        Task.WaitAll(standardOutput, standardError);
        return new(
            completed ? process.ExitCode : -1,
            standardOutput.Result,
            standardError.Result,
            !completed,
            plan.OutputFile);
    }

    /// <summary>
    /// Materializes deterministic provider inputs and executes each pipeline step without a command shell.
    /// </summary>
    public static NativeBuildPipelineResult Execute(NativeBuildPipeline pipeline, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Build timeout must be positive.");
        if (pipeline.Steps.Count == 0)
            throw new InvalidDataException("A native build pipeline must contain at least one step.");

        foreach (NativeBuildInputFile input in pipeline.InputFiles)
        {
            string fullPath = Path.GetFullPath(input.Path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, input.Content);
        }

        DateTime deadline = DateTime.UtcNow + timeout;
        List<NativeBuildStepResult> results = [];
        foreach (NativeBuildStep step in pipeline.Steps)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                results.Add(new(step.Name, -1, string.Empty, "Pipeline timeout expired before this step started.", true));
                break;
            }
            NativeBuildResult result = Execute(
                new NativeBuildPlan(pipeline.Provider, step.Executable, step.Arguments, step.WorkingDirectory, pipeline.OutputFile),
                remaining);
            results.Add(new(step.Name, result.ExitCode, result.StandardOutput, result.StandardError, result.TimedOut));
            if (result.ExitCode != 0 || result.TimedOut)
                break;
        }
        return new(pipeline.Provider, results.AsReadOnly(), results.Any(result => result.TimedOut), pipeline.OutputFile);
    }
}
