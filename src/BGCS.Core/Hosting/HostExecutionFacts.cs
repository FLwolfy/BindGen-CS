namespace BGCS.Core.Hosting;

/// <summary>Captures open host execution facts without selecting a native target or managed consumer runtime.</summary>
/// <param name="hostRuntimeIdentifier">The provider's concrete host resource identity.</param>
/// <param name="managedHostExecutable">The exact managed process host executable.</param>
/// <param name="environmentNamesCaseSensitive">Whether process environment variable names distinguish case.</param>
public sealed record HostExecutionFacts(
    string hostRuntimeIdentifier,
    string managedHostExecutable,
    bool environmentNamesCaseSensitive
);
