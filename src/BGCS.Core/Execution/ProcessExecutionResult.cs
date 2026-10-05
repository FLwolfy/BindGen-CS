namespace BGCS.Core.Execution;

/// <summary>
/// Contains drained process output after the process has exited or been terminated.
/// </summary>
/// <param name="exitCode">
/// The tool exit code, or minus one when its deadline elapsed.
/// </param>
/// <param name="standardOutput">
/// The complete standard output captured before exit.
/// </param>
/// <param name="standardError">
/// The complete standard error captured before exit.
/// </param>
/// <param name="timedOut">
/// Whether the executor terminated the process after its deadline.
/// </param>
public sealed record ProcessExecutionResult(
    int exitCode,
    string standardOutput,
    string standardError,
    bool timedOut
);
