using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using BGCS.Core.IO;

namespace BGCS.Cpp2C.Build;

/// <summary>Stages native build outputs into the NuGet multi-RID runtime asset convention.</summary>
public static class NativeAssetLayout
{
    /// <summary>
    /// File name of the current package asset index.
    /// </summary>
    public const string C_MANIFESTFILENAME = "bgcs.native-assets.json";
    /// <summary>Maps a validated BindGen-CS desktop target identifier to its .NET runtime identifier.</summary>
    /// <param name="targetIdentifier">Native target containing platform and architecture.</param>
    /// <returns>The corresponding desktop .NET runtime identifier.</returns>
    /// <exception cref="NotSupportedException">The platform or architecture has no supported native package layout.</exception>
    /// <exception cref="InvalidDataException">The identifier has no platform and architecture components.</exception>
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
            _ => throw new NotSupportedException($"Native package layout does not claim target '{targetIdentifier}'. Current production scope is Windows, Linux, and macOS desktop.")
        };
    }

    /// <summary>
    /// Copies one verified native binary to <c>runtimes/&lt;rid&gt;/native</c> and updates the deterministic
    /// package asset manifest. Repeated staging replaces only the same target/file entry.
    /// </summary>
    /// <param name="manifest">Target and library identity of the native build.</param>
    /// <param name="nativeBinary">Existing binary with the expected file name and architecture.</param>
    /// <param name="packageRoot">Package tree to replace after its complete candidate has been validated.</param>
    /// <returns>Committed binary path, index path, runtime identifier and content checksum.</returns>
    /// <exception cref="InvalidDataException">The binary or existing package index does not match its declared identity.</exception>
    /// <exception cref="FileNotFoundException">The source binary does not exist.</exception>
    /// <exception cref="IOException">Package ownership, candidate copying or publication fails.</exception>
    public static NativeAssetLayoutResult Stage(
        CppBridgeBuildManifest manifest,
        string nativeBinary,
        string packageRoot
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeBinary);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        string source = Path.GetFullPath(nativeBinary);
        if (!File.Exists(source))
            throw new FileNotFoundException("Native binary to package was not found.", source);
        string rid = GetRuntimeIdentifier(manifest.targetIdentifier);
        string expectedFileName = NativeBuildPaths.GetLibraryFileName(manifest.targetIdentifier, manifest.libraryName);
        if (!string.Equals(Path.GetFileName(source), expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Native binary '{Path.GetFileName(source)}' does not match target/library contract '{expectedFileName}'.");
        }

        NativeBinaryIdentity.Validate(source, manifest.targetIdentifier);
        string root = Path.GetFullPath(packageRoot);
        using OutputDirectoryTransaction transaction = new(root);
        string indexPath = Path.Combine(root, C_MANIFESTFILENAME);
        NativeAssetIndex index = LoadIndex(indexPath);
        CopyPackage(root, transaction.stagingPath);
        string relativePath = Path.Combine("runtimes", rid, "native", expectedFileName).Replace('\\', '/');
        string destination = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string candidate = Path.Combine(transaction.stagingPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
        File.Copy(source, candidate, true);
        string checksum;
        using (FileStream binary = File.OpenRead(candidate))
        {
            checksum = Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant();
        }
        List<NativeAssetEntry> entries = index.assets.Where(entry => !(string.Equals(entry.runtimeIdentifier, rid, StringComparison.Ordinal) && string.Equals(entry.path, relativePath, StringComparison.Ordinal))).Append(new(rid, manifest.targetIdentifier, relativePath, checksum)).OrderBy(entry => entry.runtimeIdentifier, StringComparer.Ordinal).ThenBy(entry => entry.path, StringComparer.Ordinal).ToList();
        NativeAssetIndex updated = new(entries.AsReadOnly());
        File.WriteAllText(Path.Combine(transaction.stagingPath, C_MANIFESTFILENAME),
            JsonSerializer.Serialize(updated, jsonOptions) + Environment.NewLine);
        transaction.Commit();
        return new(rid, destination, indexPath, checksum);
    }

    private static NativeAssetIndex LoadIndex(string path)
    {
        if (!File.Exists(path))
            return new([]);
        NativeAssetIndex index;
        try
        {
            index = JsonSerializer.Deserialize<NativeAssetIndex>(File.ReadAllText(path), jsonOptions)
                ?? throw new InvalidDataException($"Native asset index '{path}' is empty.");
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Native asset index '{path}' has missing asset entries.", exception);
        }
        if (index.assets == null || index.assets.Any(entry => entry == null
            || string.IsNullOrWhiteSpace(entry.runtimeIdentifier)
            || string.IsNullOrWhiteSpace(entry.targetIdentifier)
            || string.IsNullOrWhiteSpace(entry.path)
            || entry.sha256 == null || entry.sha256.Length != 64))
        {
            throw new InvalidDataException($"Native asset index '{path}' contains incomplete entries.");
        }
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        string root = Path.GetDirectoryName(path)!;
        foreach (NativeAssetEntry entry in index.assets)
        {
            string fileName = entry.path[(entry.path.LastIndexOf('/') + 1)..];
            string expectedPath = $"runtimes/{entry.runtimeIdentifier}/native/{fileName}";
            if (fileName is "" or "." or ".." || fileName.Contains('\\')
                || !string.Equals(entry.path, expectedPath, StringComparison.Ordinal)
                || !string.Equals(entry.runtimeIdentifier, GetRuntimeIdentifier(entry.targetIdentifier), StringComparison.Ordinal)
                || !identities.Add(entry.path)
                || entry.sha256.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            {
                throw new InvalidDataException($"Native asset index '{path}' contains an invalid identity: '{entry.path}'.");
            }
            string binaryPath = Path.Combine(root, entry.path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(binaryPath))
                throw new InvalidDataException($"Indexed native asset does not exist: '{binaryPath}'.");
            using FileStream binary = File.OpenRead(binaryPath);
            string actualHash = Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant();
            if (!string.Equals(entry.sha256, actualHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Indexed native asset content does not match its checksum: '{binaryPath}'.");
        }
        return index;
    }

    private static void CopyPackage(
        string source,
        string destination
    ) {
        if (!Directory.Exists(source))
            return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Native package contains a symbolic link or reparse point: '{entry}'.");
            string target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                CopyPackage(entry, target);
            }
            else
            {
                File.Copy(entry, target);
            }
        }
    }

    private static JsonSerializerOptions jsonOptions { get; } = new()
    {
        WriteIndented = true
    };
}
