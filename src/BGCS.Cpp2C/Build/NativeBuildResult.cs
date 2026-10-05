using System.IO;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Captures one completed or timed-out compiler invocation.
/// </summary>
/// <param name="exitCode">Process exit code, or a negative value when no normal exit code exists.</param>
/// <param name="standardOutput">Captured standard output.</param>
/// <param name="standardError">Captured standard error.</param>
/// <param name="timedOut">Whether execution exceeded the allowed duration.</param>
/// <param name="outputFile">Expected native artifact path.</param>
public sealed record NativeBuildResult(
    int exitCode,
    string standardOutput,
    string standardError,
    bool timedOut,
    string outputFile
)
{
    /// <summary>
    /// Gets whether the process completed successfully and its expected artifact exists.
    /// </summary>
    public bool success => !this.timedOut && this.exitCode == 0 && File.Exists(this.outputFile);
}
