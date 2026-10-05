using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using BGCS.Core.IO;

namespace BGCS.Core.Caching;

/// <summary>
/// Publishes complete content-addressed generation results and validates their bytes before restoration.
/// </summary>
public sealed class IncrementalGenerationCache
{
    private const string C_MAGIC = "BGCS.GenerationCache";
    private readonly string m_root;

    /// <summary>
    /// Selects the rebuildable cache root without creating entries or retaining in-memory publication locks.
    /// </summary>
    /// <param name="root">
    /// The cache directory; generated output must reside outside this tree.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The directory is empty or is a volume root.
    /// </exception>
    public IncrementalGenerationCache(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        m_root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _ = DirectoryPublicationLease.GetPath(m_root);
    }

    /// <summary>
    /// Hashes the generator identity, normalized input paths, file lengths and exact input bytes in stable order.
    /// </summary>
    /// <param name="generatorFingerprint">
    /// The identity of the complete generator, configuration, target and extension closure.
    /// </param>
    /// <param name="inputFiles">
    /// All source inputs; repeated absolute paths participate only once.
    /// </param>
    /// <returns>
    /// A normalized SHA-256 identity with its distinct input count.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The generator identity or an input path is empty.
    /// </exception>
    /// <exception cref="IOException">
    /// An input is missing, unreadable or changes while being hashed.
    /// </exception>
    public static IncrementalCacheKey CreateKey(
        string generatorFingerprint,
        IEnumerable<string> inputFiles
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorFingerprint);
        ArgumentNullException.ThrowIfNull(inputFiles);
        string[] files = inputFiles.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, C_MAGIC);
        Add(hash, generatorFingerprint);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        Span<byte> length = stackalloc byte[sizeof(long)];
        try
        {
            foreach (string file in files)
            {
                Add(hash, file.Replace('\\', '/'));
                using FileStream stream = File.OpenRead(file);
                long expectedLength = stream.Length;
                DateTime modified = File.GetLastWriteTimeUtc(file);
                BinaryPrimitives.WriteInt64LittleEndian(length, expectedLength);
                hash.AppendData(length);
                long total = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    total += read;
                }
                if (total != expectedLength || stream.Length != expectedLength || File.GetLastWriteTimeUtc(file) != modified)
                    throw new IOException($"Generation input changed while hashing: '{file}'.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return new IncrementalCacheKey(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), files.Length);
    }

    /// <summary>
    /// Restores a verified entry under publication ownership, preserving current output when the entry is unavailable or damaged.
    /// </summary>
    /// <param name="key">
    /// The frozen generation identity to restore.
    /// </param>
    /// <param name="outputPath">
    /// The destination outside the cache tree, replaced only after the complete candidate is copied and verified.
    /// </param>
    /// <param name="metadata">
    /// Receives verified generation metadata on success, or an empty string on a cache miss.
    /// </param>
    /// <returns>
    /// True after complete restoration; false for a missing, malformed or content-mismatched entry.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The destination overlaps the cache tree.
    /// </exception>
    /// <exception cref="IOException">
    /// Publication ownership, copying or output installation fails.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Another process retains publication ownership for more than five minutes.
    /// </exception>
    public bool TryRestore(
        IncrementalCacheKey key,
        string outputPath,
        out string metadata
    ) {
        ArgumentNullException.ThrowIfNull(key);
        string output = ResolveOutput(outputPath);
        string entry = Path.Combine(m_root, key.value);
        metadata = string.Empty;
        if (!Directory.Exists(entry))
            return false;
        using var ownership = new DirectoryPublicationOwnership(entry, output);
        CacheEntry? descriptor = ReadComplete(entry, key);
        if (descriptor is null)
            return false;
        using var transaction = new OutputDirectoryTransaction(output, ownership.GetLease(output));
        CopyDirectory(Path.Combine(entry, "files"), transaction.stagingPath);
        if (!MatchesFiles(transaction.stagingPath, descriptor.files))
            return false;
        transaction.Commit();
        metadata = descriptor.metadata;
        return true;
    }

    /// <summary>
    /// Copies a successful output under publication ownership and installs an immutable, verified cache entry.
    /// </summary>
    /// <param name="key">
    /// The identity of the completed generation.
    /// </param>
    /// <param name="outputPath">
    /// The complete generated output outside the cache tree.
    /// </param>
    /// <param name="metadata">
    /// Neutral generation metadata restored together with verified output bytes.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Output overlaps the cache tree.
    /// </exception>
    /// <exception cref="IOException">
    /// Output is missing, changes during copying or cannot be published.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Another process retains publication ownership for more than five minutes.
    /// </exception>
    public void Store(
        IncrementalCacheKey key,
        string outputPath,
        string metadata
    ) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(metadata);
        string source = ResolveOutput(outputPath);
        string entry = Path.Combine(m_root, key.value);
        using var ownership = new DirectoryPublicationOwnership(entry, source);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Generated output directory not found: '{source}'.");
        if (ReadComplete(entry, key) is not null)
            return;
        using var transaction = new OutputDirectoryTransaction(entry, ownership.GetLease(entry));
        string files = Path.Combine(transaction.stagingPath, "files");
        CopyDirectory(source, files);
        CacheFile[] contents = DescribeFiles(files);
        if (!MatchesFiles(source, contents))
            throw new IOException($"Generated output changed while caching: '{source}'.");
        var descriptor = new CacheEntry(C_MAGIC, key.value, key.inputFileCount, metadata, Digest(metadata), contents);
        File.WriteAllText(Path.Combine(transaction.stagingPath, "entry.json"), JsonSerializer.Serialize(descriptor));
        transaction.Commit();
    }

    /// <summary>
    /// Enumerates C/C++ source files and extensionless SDK headers in deterministic absolute-path order.
    /// </summary>
    /// <param name="explicitFiles">
    /// Required source files, retained even when their extension is not a recognized header extension.
    /// </param>
    /// <param name="includeDirectories">
    /// Header roots searched recursively; nonexistent roots contribute no files.
    /// Directory links are followed once per resolved directory, including links outside a root.
    /// </param>
    /// <param name="excludedDirectories">
    /// Optional generated or cached subtrees excluded from include-root discovery.
    /// </param>
    /// <returns>
    /// Distinct normalized paths sorted ordinally; files are not opened by discovery.
    /// </returns>
    public static IReadOnlyList<string> DiscoverInputs(
        IEnumerable<string> explicitFiles,
        IEnumerable<string> includeDirectories,
        IEnumerable<string>? excludedDirectories = null
    ) {
        ArgumentNullException.ThrowIfNull(explicitFiles);
        ArgumentNullException.ThrowIfNull(includeDirectories);
        HashSet<string> files = explicitFiles.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        string[] exclusions = (excludedDirectories ?? []).Select(static path => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
            { ".h", ".hh", ".hpp", ".hxx", ".inc", ".inl", ".c", ".cc", ".cpp", ".cxx" };
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        HashSet<string> visited = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Stack<string> pending = new(includeDirectories.Select(Path.GetFullPath));
        while (pending.TryPop(out string? directory))
        {
            if (!Directory.Exists(directory) || IsExcluded(directory))
                continue;
            DirectoryInfo info = new(directory);
            string resolved = (info.ResolveLinkTarget(returnFinalTarget: true) ?? info).FullName;
            if (IsExcluded(resolved) || !visited.Add(resolved))
                continue;
            foreach (FileSystemInfo entry in new DirectoryInfo(resolved).EnumerateFileSystemInfos())
            {
                if (IsExcluded(entry.FullName))
                    continue;
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry.FullName);
                else if (extensions.Contains(entry.Extension) || !Path.HasExtension(entry.FullName))
                    files.Add(entry.FullName);
            }
        }
        return files.OrderBy(static path => path, StringComparer.Ordinal).ToArray();

        bool IsExcluded(string path) => exclusions.Any(exclusion =>
            path.StartsWith(exclusion, comparison) || string.Equals(path, exclusion[..^1], comparison));
    }

    private string ResolveOutput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string output = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(output, m_root, comparison) || output.StartsWith(m_root + Path.DirectorySeparatorChar, comparison)
            || m_root.StartsWith(output + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Generated output must not overlap its incremental cache tree.", nameof(path));
        return output;
    }

    private static CacheEntry? ReadComplete(
        string entry,
        IncrementalCacheKey key
    ) {
        string marker = Path.Combine(entry, "entry.json");
        if (!File.Exists(marker))
            return null;
        try
        {
            CacheEntry? descriptor = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(marker));
            return descriptor is not null && descriptor.magic == C_MAGIC && descriptor.key == key.value
                && descriptor.inputFileCount == key.inputFileCount && descriptor.metadata is not null
                && descriptor.metadataFingerprint == Digest(descriptor.metadata) && descriptor.files is not null
                && MatchesFiles(Path.Combine(entry, "files"), descriptor.files) ? descriptor : null;
        }
        catch (Exception failure) when (failure is JsonException or IOException)
        {
            return null;
        }
    }

    private static CacheFile[] DescribeFiles(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                .Any(static path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Generation cache trees cannot contain symbolic links or reparse points.");
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(static file => file, StringComparer.Ordinal).Select(file =>
            {
                using FileStream stream = File.OpenRead(file);
                return new CacheFile(Path.GetRelativePath(directory, file).Replace('\\', '/'), stream.Length,
                    Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
            }).ToArray();
    }

    private static bool MatchesFiles(
        string directory,
        CacheFile[] files
    ) => Directory.Exists(directory) && DescribeFiles(directory).SequenceEqual(files);

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Add(
        IncrementalHash hash,
        string value
    ) {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void CopyDirectory(
        string source,
        string destination
    ) {
        _ = DescribeFiles(source);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed record CacheEntry(
        string magic,
        string key,
        int inputFileCount,
        string metadata,
        string metadataFingerprint,
        CacheFile[] files
    );

    private sealed record CacheFile(
        string path,
        long length,
        string fingerprint
    );
}
