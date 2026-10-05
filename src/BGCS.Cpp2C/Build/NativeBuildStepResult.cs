namespace BGCS.Cpp2C.Build;

/// <summary>
/// Captures output and completion status for one pipeline step.
/// </summary>
/// <param name="name">Diagnostic name identifying the executed step.</param>
/// <param name="exitCode">Process exit code, or a negative value when no normal exit exists.</param>
/// <param name="standardOutput">Captured standard output.</param>
/// <param name="standardError">Captured standard error.</param>
/// <param name="timedOut">Whether the step exhausted its allowed duration.</param>
public sealed record NativeBuildStepResult(
    string name,
    int exitCode,
    string standardOutput,
    string standardError,
    bool timedOut
);
