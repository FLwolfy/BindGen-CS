namespace BGCS.Cpp2C.Build;

/// <summary>
/// Describes a deterministic input materialized before a pipeline starts.
/// </summary>
/// <param name="path">Destination file path.</param>
/// <param name="content">Complete UTF-8 text written to the input file.</param>
public sealed record NativeBuildInputFile(
    string path,
    string content
);
