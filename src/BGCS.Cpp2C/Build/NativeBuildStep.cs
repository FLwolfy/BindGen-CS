using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Freezes one process invocation in a native build pipeline.
/// </summary>
/// <param name="name">Stable diagnostic name identifying this step.</param>
/// <param name="executable">Executable invoked directly without a shell.</param>
/// <param name="arguments">Individual process arguments, copied into a read-only snapshot.</param>
/// <param name="workingDirectory">Working directory for this step.</param>
public sealed record NativeBuildStep(
    string name,
    string executable,
    IReadOnlyList<string> arguments,
    string workingDirectory
)
{
    /// <summary>
    /// Individual process arguments, copied into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> arguments { get; } = Array.AsReadOnly((arguments ?? throw new ArgumentNullException(nameof(arguments))).ToArray());

}
