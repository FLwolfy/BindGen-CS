using System.Security.Cryptography;
using System.Text.Json;

namespace BGCS.Cpp2C.Build;

/// <summary>Stages native build outputs into the NuGet multi-RID runtime asset convention.</summary>
public static class NativeAssetLayout
{
    public const int CurrentLayoutVersion = 1;
    public const string ManifestFileName = "bgcs.native-assets.json";

    /// <summary>Maps a validated BindGen-CS desktop target identifier to its .NET runtime identifier.</summary>
    public static string GetRuntimeIdentifier(string targetIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIdentifier);
        string[] parts = targetIdentifier.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new InvalidDataException($"Target identifier '{targetIdentifier}' does not include a platform and architecture.");
        string platform = parts[0].ToLowerInvariant();
        string architecture = parts[1].ToLowerInvariant();
        if (architecture is not ("x64" or "arm64"))
            throw new NotSupportedException($"Native package layout does not support architecture '{architecture}'. Supported desktop architectures are x64 and arm64.");
        return platform switch
        {
            "windows" => $"win-{architecture}",
            "linux" => $"linux-{architecture}",
            "macos" => $"osx-{architecture}",
            _ => throw new NotSupportedException(
                $"Native package layout does not claim target '{targetIdentifier}'. Current production scope is Windows, Linux, and macOS desktop.")
        };
    }

    /// <summary>
    /// Copies one verified native binary to <c>runtimes/&lt;rid&gt;/native</c> and updates the deterministic
    /// package asset manifest. Repeated staging replaces only the same target/file entry.
    /// </summary>
    public static NativeAssetLayoutResult Stage(CppBridgeBuildManifest manifest, string nativeBinary,
        string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeBinary);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        string source = Path.GetFullPath(nativeBinary);
        if (!File.Exists(source))
            throw new FileNotFoundException("Native binary to package was not found.", source);
        string rid = GetRuntimeIdentifier(manifest.TargetIdentifier);
        string expectedFileName = NativeBuildPaths.GetLibraryFileName(manifest.TargetIdentifier, manifest.LibraryName);
        if (!string.Equals(Path.GetFileName(source), expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Native binary '{Path.GetFileName(source)}' does not match target/library contract '{expectedFileName}'.");
        }
        NativeBinaryIdentity.Validate(source, manifest.TargetIdentifier);

        string root = Path.GetFullPath(packageRoot);
        string relativePath = Path.Combine("runtimes", rid, "native", expectedFileName).Replace('\\', '/');
        string destination = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!string.Equals(source, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, true);
        string checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant();

        string indexPath = Path.Combine(root, ManifestFileName);
        NativeAssetIndex index = LoadIndex(indexPath);
        List<NativeAssetEntry> entries = index.Assets
            .Where(entry => !(string.Equals(entry.RuntimeIdentifier, rid, StringComparison.Ordinal) &&
                string.Equals(entry.Path, relativePath, StringComparison.Ordinal)))
            .Append(new(rid, manifest.TargetIdentifier, relativePath, checksum))
            .OrderBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
        NativeAssetIndex updated = new(CurrentLayoutVersion, entries);
        Directory.CreateDirectory(root);
        File.WriteAllText(indexPath, JsonSerializer.Serialize(updated, JsonOptions) + Environment.NewLine);
        return new(rid, destination, indexPath, checksum);
    }

    private static NativeAssetIndex LoadIndex(string path)
    {
        if (!File.Exists(path))
            return new(CurrentLayoutVersion, []);
        NativeAssetIndex index = JsonSerializer.Deserialize<NativeAssetIndex>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Native asset index '{path}' is empty.");
        if (index.LayoutVersion != CurrentLayoutVersion)
            throw new InvalidDataException($"Native asset layout version {index.LayoutVersion} is not supported.");
        return index;
    }

    private static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };
}

/// <summary>Describes one staged native runtime asset.</summary>
public sealed record NativeAssetLayoutResult(string RuntimeIdentifier, string AssetPath, string ManifestPath,
    string Sha256);

/// <summary>Machine-readable index stored beside a multi-RID runtime asset tree.</summary>
public sealed record NativeAssetIndex(int LayoutVersion, IReadOnlyList<NativeAssetEntry> Assets);

/// <summary>One deterministic multi-RID native asset index entry.</summary>
public sealed record NativeAssetEntry(string RuntimeIdentifier, string TargetIdentifier, string Path, string Sha256);
