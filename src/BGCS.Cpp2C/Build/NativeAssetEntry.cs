namespace BGCS.Cpp2C.Build;

/// <summary>
/// Records the identity and checksum of one native asset in a package.
/// </summary>
/// <param name="runtimeIdentifier">
/// Runtime identifier selecting this native asset.
/// </param>
/// <param name="targetIdentifier">
/// Native ABI target used to compile the asset.
/// </param>
/// <param name="path">
/// Package-relative path under runtimes/&lt;rid&gt;/native, with forward slashes.
/// </param>
/// <param name="sha256">
/// Lowercase SHA-256 digest of the indexed binary.
/// </param>
public sealed record NativeAssetEntry(
    string runtimeIdentifier,
    string targetIdentifier,
    string path,
    string sha256
);
