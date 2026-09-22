using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BGCS.Core.IO;

namespace BGCS.Core.Caching;

/// <summary>Content-addressed key for one complete generation result.</summary>
public sealed record IncrementalCacheKey(string Value, int InputFileCount);

/// <summary>
/// Immutable output cache with atomic publication and restoration. Cache entries are never modified after publication.
/// </summary>
public sealed class IncrementalGenerationCache
{
    private const int FormatVersion = 1;
    private static readonly ConcurrentDictionary<string, object> EntryLocks = new(StringComparer.Ordinal);
    private readonly string root;

    public IncrementalGenerationCache(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
    }

    /// <summary>Computes a SHA-256 key from the generator fingerprint and exact contents of all inputs.</summary>
    public static IncrementalCacheKey CreateKey(string generatorFingerprint, IEnumerable<string> inputFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorFingerprint);
        ArgumentNullException.ThrowIfNull(inputFiles);
        string[] files = inputFiles.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "BGCS incremental cache v" + FormatVersion);
        Add(hash, generatorFingerprint);
        foreach (string file in files)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"Incremental cache input not found: {file}", file);
            Add(hash, file.Replace('\\', '/'));
            using FileStream stream = File.OpenRead(file);
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
        return new(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), files.Length);
    }

    /// <summary>Restores an entry into the requested destination using an output transaction.</summary>
    public bool TryRestore(IncrementalCacheKey key, string outputPath, out string metadata)
    {
        ArgumentNullException.ThrowIfNull(key);
        string entry = GetEntryPath(key);
        string marker = Path.Combine(entry, "entry.json");
        string files = Path.Combine(entry, "files");
        if (!File.Exists(marker) || !Directory.Exists(files))
        {
            metadata = string.Empty;
            return false;
        }
        lock (EntryLocks.GetOrAdd(entry, static _ => new()))
        {
            CacheEntry? descriptor = System.Text.Json.JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(marker));
            if (descriptor is not { FormatVersion: FormatVersion } || !string.Equals(descriptor.Key, key.Value, StringComparison.Ordinal))
            {
                metadata = string.Empty;
                return false;
            }
            using OutputDirectoryTransaction transaction = new(outputPath);
            CopyDirectory(files, transaction.StagingPath);
            transaction.Commit();
            metadata = descriptor.Metadata;
            return true;
        }
    }

    /// <summary>Publishes a complete successful output directory if the content-addressed entry does not exist.</summary>
    public void Store(IncrementalCacheKey key, string outputPath, string metadata)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string source = Path.GetFullPath(outputPath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Generated output directory not found: {source}");
        Directory.CreateDirectory(root);
        string entry = GetEntryPath(key);
        lock (EntryLocks.GetOrAdd(entry, static _ => new()))
        {
            if (File.Exists(Path.Combine(entry, "entry.json")))
                return;
            string staging = Path.Combine(root, ".publish-" + key.Value + "-" + Guid.NewGuid().ToString("N"));
            try
            {
                string files = Path.Combine(staging, "files");
                Directory.CreateDirectory(files);
                CopyDirectory(source, files);
                File.WriteAllText(Path.Combine(staging, "entry.json"), System.Text.Json.JsonSerializer.Serialize(
                    new CacheEntry(FormatVersion, key.Value, key.InputFileCount, metadata)));
                try
                {
                    Directory.Move(staging, entry);
                }
                catch (IOException) when (Directory.Exists(entry))
                {
                    // A process outside this runtime won the immutable publication race.
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
        }
    }

    /// <summary>Enumerates C/C++ source-like inputs recursively with deterministic ordering.</summary>
    public static IReadOnlyList<string> DiscoverInputs(IEnumerable<string> explicitFiles, IEnumerable<string> includeDirectories,
        IEnumerable<string>? excludedDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(explicitFiles);
        ArgumentNullException.ThrowIfNull(includeDirectories);
        HashSet<string> files = explicitFiles.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        string[] exclusions = (excludedDirectories ?? []).Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();
        HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".h", ".hh", ".hpp", ".hxx", ".inc", ".inl", ".c", ".cc", ".cpp", ".cxx"
        };
        foreach (string directory in includeDirectories.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            if (!Directory.Exists(directory))
                continue;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                if (extensions.Contains(Path.GetExtension(file)) && !exclusions.Any(exclusion => Path.GetFullPath(file).StartsWith(exclusion, StringComparison.Ordinal)))
                    files.Add(Path.GetFullPath(file));
        }
        return files.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private string GetEntryPath(IncrementalCacheKey key) => Path.Combine(root, key.Value);

    private static void Add(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private sealed record CacheEntry(int FormatVersion, string Key, int InputFileCount, string Metadata);
}
