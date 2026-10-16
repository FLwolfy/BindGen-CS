using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;

namespace BGCS.Infrastructure.IO;

/// <summary>Captures extension inputs by bytes, complete directory membership and tracked absence.</summary>
public sealed class ExternalInputSnapshotBuilder
{
    /// <summary>Captures all explicitly declared external files and complete directory content closures.</summary>
    /// <param name="inputs">The complete immutable declarations relevant to the affected stage.</param>
    /// <param name="pathBase">The captured absolute base for local input paths.</param>
    /// <param name="cancellationToken">Cancels traversal and hashing.</param>
    /// <returns>A detached snapshot that can be rechecked before cache reuse or publication.</returns>
    public SourceSnapshot Capture(
        ImmutableArray<DeclaredExternalInput> inputs,
        string pathBase,
        CancellationToken cancellationToken = default
    ) {
        if (inputs.IsDefault) throw new ArgumentException("External inputs must be an initialized immutable collection.", nameof(inputs));
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        if (!Path.IsPathFullyQualified(pathBase)) throw new ArgumentException("External inputs require an absolute captured path base.", nameof(pathBase));
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, InputDependency> files = new(comparer);
        Dictionary<string, DirectoryInputDependency> directories = new(comparer);
        Dictionary<string, MissingInputDependency> missing = new(comparer);
        foreach (DeclaredExternalInput input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(input);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.path);
            if (!Enum.IsDefined(input.kind)) throw new ArgumentException("An external input kind is unknown.", nameof(inputs));
            string path = Path.GetFullPath(input.path, pathBase);
            bool exists = input.kind == ExternalInputKind.File ? File.Exists(path) : Directory.Exists(path);
            if (!exists)
            {
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("An external input has the wrong filesystem kind.");
                if (!input.allowMissing) throw new FileNotFoundException("A required external input is missing.", path);
                missing[path] = new(path, PhysicalPathResolver.Resolve(path));
                continue;
            }
            if (input.kind == ExternalInputKind.File) AddFile(path);
            else
            {
                directories[path] = DirectoryMembershipDigest.Capture(path, cancellationToken: cancellationToken);
                HashSet<string> visited = new(comparer);
                Stack<string> pending = new();
                pending.Push(path);
                while (pending.TryPop(out string? directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(PhysicalPathResolver.Resolve(directory))) continue;
                    foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((entry.Attributes & FileAttributes.Directory) != 0) pending.Push(entry.FullName);
                        else AddFile(entry.FullName);
                    }
                }
            }
        }
        SourceSnapshot snapshot = new(files.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray(),
            directories.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray())
        { absentInputs = missing.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray() };
        if (!SourceSnapshotVerifier.Matches(snapshot, cancellationToken)) throw new IOException("External inputs changed while their snapshot was being captured.");
        return snapshot;

        void AddFile(string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string physical = PhysicalPathResolver.Resolve(path);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
            InputDependency dependency = new(path, physical, stream.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
            if (files.TryGetValue(path, out InputDependency? previous) && previous != dependency) throw new IOException("An external file changed during dependency discovery.");
            files[path] = dependency;
        }
    }
}
