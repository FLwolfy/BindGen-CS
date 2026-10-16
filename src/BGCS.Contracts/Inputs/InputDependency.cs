using BGCS.Contracts.Common;

namespace BGCS.Contracts.Inputs;

/// <summary>Records the exact bytes read by the native parser and their filesystem identity.</summary>
/// <param name="path">The absolute logical input path.</param>
/// <param name="resolvedPath">The physical path after resolving links.</param>
/// <param name="length">The parsed byte count.</param>
/// <param name="digest">The digest of the parser's original input buffer.</param>
public sealed record InputDependency(
    string path,
    string resolvedPath,
    long length,
    ContentDigest digest
);

/// <summary>Records a search directory's existence, links and complete recursive membership.</summary>
/// <param name="path">The declared search root.</param>
/// <param name="resolvedPath">The root's resolved physical identity.</param>
/// <param name="exists">Whether the search root existed when captured.</param>
/// <param name="digest">The complete directory-membership digest.</param>
public sealed record DirectoryInputDependency(
    string path,
    string resolvedPath,
    bool exists,
    ContentDigest digest
);

/// <summary>Tracks an explicitly declared missing file or directory so later creation invalidates reuse.</summary>
/// <param name="path">The absolute logical input that did not exist.</param>
/// <param name="resolvedPath">The physical identity of the missing path through its existing ancestors.</param>
public sealed record MissingInputDependency(
    string path,
    string resolvedPath
);
