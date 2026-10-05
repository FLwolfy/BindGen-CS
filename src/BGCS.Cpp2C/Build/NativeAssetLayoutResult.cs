namespace BGCS.Cpp2C.Build;

/// <summary>
/// Describes the committed runtime binary and its package index.
/// </summary>
/// <param name="runtimeIdentifier">
/// Runtime identifier of the staged binary.
/// </param>
/// <param name="assetPath">
/// Absolute path of the committed binary.
/// </param>
/// <param name="manifestPath">
/// Absolute path of the committed package index.
/// </param>
/// <param name="sha256">
/// Lowercase SHA-256 digest of the committed binary.
/// </param>
public sealed record NativeAssetLayoutResult(
    string runtimeIdentifier,
    string assetPath,
    string manifestPath,
    string sha256
);
