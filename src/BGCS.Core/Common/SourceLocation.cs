namespace BGCS.Core.Common;

/// <summary>Locates a fact in a logical source file without retaining parser handles.</summary>
/// <param name="file">The normalized logical source path.</param>
/// <param name="line">The one-based line, or zero for a synthetic fact.</param>
/// <param name="column">The one-based column, or zero for a synthetic fact.</param>
public readonly record struct SourceLocation(
    string file,
    int line,
    int column
);
