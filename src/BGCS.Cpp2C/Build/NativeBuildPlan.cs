using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Freezes one shell-independent native compiler invocation.
/// </summary>
/// <param name="provider">Provider that produced this plan.</param>
/// <param name="executable">Compiler driver executable or absolute path.</param>
/// <param name="arguments">Individual process arguments, copied into a read-only snapshot; shell parsing is never required.</param>
/// <param name="workingDirectory">Process working directory.</param>
/// <param name="outputFile">Expected native artifact path.</param>
public sealed record NativeBuildPlan(
    string provider,
    string executable,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    string outputFile
)
{
    /// <summary>
    /// Individual process arguments, copied into a read-only snapshot; shell parsing is never required.
    /// </summary>
    public IReadOnlyList<string> arguments { get; } = Array.AsReadOnly((arguments ?? throw new ArgumentNullException(nameof(arguments))).ToArray());

}
