using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace BGCS.Frontend.Clang.Interop;

internal static class ClangResourceHeaders
{
    private const string C_RESOURCE_NAME = "BGCS.Frontend.Clang.Clang.Headers.zip";
    private static readonly Lazy<string> m_directory = new(Extract);
    internal static string directory => m_directory.Value;

    internal static string VerifyDirectory()
    {
        string current = directory;
        using Stream resource = typeof(ClangResourceHeaders).Assembly.GetManifestResourceStream(C_RESOURCE_NAME)
            ?? throw new InvalidOperationException("The parser's Clang resource headers are missing from its assembly.");
        return EnsureExtracted(resource, Path.GetDirectoryName(current)!);
    }

    private static string Extract()
    {
        using Stream resource = typeof(ClangResourceHeaders).Assembly.GetManifestResourceStream(C_RESOURCE_NAME) ?? throw new InvalidOperationException("The parser's Clang resource headers are missing from its assembly.");
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGCS", "ClangResources");
        return EnsureExtracted(resource, cache);
    }

    // Each worker verifies its extraction independently. A ZIP identity does not
    // establish the identity of bytes subsequently stored in a writable cache.
    internal static string EnsureExtracted(
        Stream resource,
        string cacheRoot
    ) {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        if (!resource.CanSeek) throw new ArgumentException("The locked parser resource stream must support seeking.", nameof(resource));
        resource.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(resource));
        string cache = Path.GetFullPath(cacheRoot);
        string destination = Path.Combine(cache, hash);
        Directory.CreateDirectory(cache);
        CheckDirectory(cache);
        resource.Position = 0;
        using ZipArchive archive = new(resource, ZipArchiveMode.Read, leaveOpen: true);
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, ZipArchiveEntry> files = new(comparer);
        HashSet<string> directories = new(comparer);
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            bool isDirectory = name.EndsWith("/", StringComparison.Ordinal);
            string[] parts = name.TrimEnd('/').Split('/');
            if (parts.Length == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."
                || part.Contains(':') || part.Contains('\0')) || Path.IsPathRooted(name))
                throw new InvalidDataException("A locked Clang resource entry contains an unsafe relative path.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The locked Clang resource bundle cannot contain symbolic links.");
            string relative = string.Join(Path.DirectorySeparatorChar, parts);
            if (isDirectory)
            {
                if (files.ContainsKey(relative)) throw new InvalidDataException("A locked Clang resource path has conflicting kinds.");
                directories.Add(relative);
            }
            else
            {
                total = checked(total + entry.Length);
                if (entry.Length > 128L * 1024 * 1024 || total > 512L * 1024 * 1024 || files.Count >= 10000)
                    throw new InvalidDataException("The locked Clang resource bundle exceeds extraction limits.");
                if (directories.Contains(relative) || !files.TryAdd(relative, entry))
                    throw new InvalidDataException("The locked Clang resource bundle contains conflicting paths.");
            }
            for (int index = 1; index < parts.Length; index++)
            {
                string parent = string.Join(Path.DirectorySeparatorChar, parts.Take(index));
                if (files.ContainsKey(parent)) throw new InvalidDataException("A locked Clang resource file is also used as a directory.");
                directories.Add(parent);
            }
        }
        if (!files.ContainsKey(Path.Combine("include", "stddef.h")))
            throw new InvalidDataException("The locked Clang resource bundle omitted its required builtin headers.");
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            Verify(destination);
            return destination;
        }
        string staging = Path.Combine(cache, hash + "." + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            foreach (string directory in directories.OrderBy(path => path.Length)) Directory.CreateDirectory(Path.Combine(staging, directory));
            foreach ((string relative, ZipArchiveEntry entry) in files)
            {
                using Stream input = entry.Open();
                using FileStream output = new(Path.Combine(staging, relative), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            Verify(staging);
            try
            {
                Directory.Move(staging, destination);
            }
            catch (IOException) when (Directory.Exists(destination))
            {
                // Accept a competing publication only after independently
                // establishing the complete content identity, never a sentinel.
                Verify(destination);
            }
            Verify(destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                string resolvedStaging = Path.GetFullPath(staging);
                if (!comparer.Equals(Path.GetDirectoryName(resolvedStaging), cache))
                    throw new InvalidOperationException("The parser resource staging directory escaped its owned cache.");
                Directory.Delete(staging, recursive: true);
            }
        }

        void Verify(string root)
        {
            CheckDirectory(root);
            HashSet<string> seenFiles = new(comparer), seenDirectories = new(comparer);
            Stack<string> pending = new();
            pending.Push(root);
            while (pending.TryPop(out string? current))
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("The Clang resource cache contains a reparse point; refusing to follow it.");
                    string relative = Path.GetRelativePath(root, path);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!directories.Contains(relative) || !seenDirectories.Add(relative))
                            throw new InvalidDataException("The Clang resource cache contains an unexpected directory.");
                        pending.Push(path);
                    }
                    else
                    {
                        if (!files.TryGetValue(relative, out ZipArchiveEntry? expected) || !seenFiles.Add(relative))
                            throw new InvalidDataException("The Clang resource cache contains an unexpected file.");
                        using FileStream actual = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using Stream locked = expected.Open();
                        if (actual.Length != expected.Length || !SHA256.HashData(actual).AsSpan().SequenceEqual(SHA256.HashData(locked)))
                            throw new InvalidDataException("The Clang resource cache does not match its locked header bundle; remove this owned cache entry and retry.");
                    }
                }
            }
            if (seenFiles.Count != files.Count || seenDirectories.Count != directories.Count)
                throw new InvalidDataException("The Clang resource cache omitted required files or directories; remove this owned cache entry and retry.");
        }

        static void CheckDirectory(string path)
        {
            if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Clang resource cache must be a physical owned directory.");
        }
    }
}
