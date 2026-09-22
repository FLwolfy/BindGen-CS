namespace BGCS.Cpp2C.Build;

/// <summary>
/// Immutable, shell-independent native compiler invocation.
/// </summary>
/// <param name="Provider">Build provider that produced the plan.</param>
/// <param name="Executable">Compiler driver executable or absolute path.</param>
/// <param name="Arguments">Individual process arguments; no shell parsing is required.</param>
/// <param name="WorkingDirectory">Process working directory.</param>
/// <param name="OutputFile">Expected native artifact.</param>
public sealed record NativeBuildPlan(
    string Provider,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string OutputFile);

/// <summary>
/// Result from executing one native build plan.
/// </summary>
public sealed record NativeBuildResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string OutputFile)
{
    public bool Success => !TimedOut && ExitCode == 0 && File.Exists(OutputFile);
}

/// <summary>
/// Converts a bridge build manifest into a concrete, shell-independent compiler plan.
/// </summary>
public interface INativeBuildProvider
{
    string Name { get; }
    NativeBuildPlan CreatePlan(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null);
}

/// <summary>
/// One process invocation in a native build pipeline.
/// </summary>
public sealed record NativeBuildStep(
    string Name,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

/// <summary>
/// A file that must be materialized before a native build pipeline starts.
/// </summary>
public sealed record NativeBuildInputFile(string Path, string Content);

/// <summary>
/// Immutable, shell-independent description of a potentially multi-step native build.
/// </summary>
public sealed record NativeBuildPipeline(
    string Provider,
    IReadOnlyList<NativeBuildInputFile> InputFiles,
    IReadOnlyList<NativeBuildStep> Steps,
    string OutputFile)
{
    /// <summary>Single-step executable, retained for simple plan consumers.</summary>
    public string? Executable => Steps.Count == 1 ? Steps[0].Executable : null;

    /// <summary>Single-step arguments, retained for simple plan consumers.</summary>
    public IReadOnlyList<string>? Arguments => Steps.Count == 1 ? Steps[0].Arguments : null;

    /// <summary>Single-step working directory, retained for simple plan consumers.</summary>
    public string? WorkingDirectory => Steps.Count == 1 ? Steps[0].WorkingDirectory : null;
}

/// <summary>
/// Result from executing a native build pipeline.
/// </summary>
public sealed record NativeBuildPipelineResult(
    string Provider,
    IReadOnlyList<NativeBuildStepResult> Steps,
    bool TimedOut,
    string OutputFile)
{
    public bool Success => !TimedOut && Steps.Count > 0 && Steps.All(step => step.ExitCode == 0) && File.Exists(OutputFile);
}

/// <summary>
/// Captured output for one native build pipeline step.
/// </summary>
public sealed record NativeBuildStepResult(
    string Name,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

/// <summary>
/// Converts a bridge manifest into a shell-independent, potentially multi-step build pipeline.
/// </summary>
public interface INativeBuildPipelineProvider
{
    string Name { get; }
    NativeBuildPipeline CreatePipeline(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null);
}
