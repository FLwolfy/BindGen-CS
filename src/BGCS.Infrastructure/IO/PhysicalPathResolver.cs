using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BGCS.Core.Hosting;

namespace BGCS.Infrastructure.IO;

/// <summary>Resolves symbolic links at every path component instead of only the final file.</summary>
public static class PhysicalPathResolver
{
    /// <summary>Resolves existing links while preserving nonexistent trailing components.</summary>
    /// <param name="path">The logical path.</param>
    /// <returns>The absolute physical identity.</returns>
    /// <exception cref="IOException">A link is cyclic or cannot be resolved.</exception>
    /// <param name="paths">The explicitly selected host path semantics.</param>
    public static string Resolve(
        string path,
        IHostPathService paths
    )
        => ResolvePath(path, paths, null, CancellationToken.None);

    private static string ResolvePath(
        string path,
        IHostPathService paths,
        ResolutionScope? scope,
        CancellationToken cancellationToken
    )
        => ResolveCore(Path.GetFullPath(path), new HashSet<string>(paths.comparer), 0, scope, cancellationToken);

    private static string ResolveCore(
        string absolute,
        HashSet<string> activeLinks,
        int depth,
        ResolutionScope? scope,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 128) throw new IOException("The path exceeds the symbolic-link resolution limit.");
        string root = Path.GetPathRoot(absolute)!;
        string current = root;
        foreach (string component in absolute[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = Path.Combine(current, component);
            ComponentObservation observation = scope is null ? ReadObservation(current) : scope.Observe(current);
            if (observation.linkTarget is string target)
            {
                string link = current;
                if (!activeLinks.Add(link)) throw new IOException($"Input link is cyclic: '{link}'.");
                try
                {
                    // Always replay target recursion and active-link checks, including
                    // when this component's metadata belongs to a query-local scope.
                    current = ResolveCore(Path.GetFullPath(target, Path.GetDirectoryName(link)!), activeLinks, depth + 1, scope, cancellationToken);
                }
                finally { activeLinks.Remove(link); }
            }
        }
        return Path.GetFullPath(current);
    }

    private static ComponentObservation ReadObservation(string path)
    {
        FileAttributes? attributes = null;
        try { attributes = File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        string? target = null;
        if (attributes is null || (attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            FileSystemInfo entry = attributes is not null && (attributes.Value & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            target = entry.LinkTarget;
        }
        return new(attributes, target);
    }

    internal readonly record struct ComponentObservation(
        FileAttributes? attributes,
        string? linkTarget
    );

    /// <summary>Reuses component metadata during one membership walk and independently rechecks it before that query returns.</summary>
    /// <remarks>This scope is not an atomic filesystem snapshot and does not claim to detect changes restored before revalidation.</remarks>
    internal sealed class ResolutionScope
    {
        private readonly IHostPathService m_paths;
        private readonly Dictionary<string, ComponentObservation> m_observations = new(StringComparer.Ordinal);
        private bool m_closed;

        internal ResolutionScope(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));

        internal string Resolve(
            string path,
            CancellationToken cancellationToken
        ) {
            if (m_closed) throw new InvalidOperationException("The physical path observation scope has already closed.");
            return ResolvePath(path, m_paths, this, cancellationToken);
        }

        internal ComponentObservation Observe(string path)
        {
            if (m_closed) throw new InvalidOperationException("The physical path observation scope has already closed.");
            if (!m_observations.TryGetValue(path, out ComponentObservation observation))
            {
                observation = ReadObservation(path);
                m_observations.Add(path, observation);
            }
            return observation;
        }

        internal void Verify(CancellationToken cancellationToken)
        {
            if (m_closed) throw new InvalidOperationException("The physical path observation scope has already closed.");
            m_closed = true;
            try
            {
                foreach (var component in m_observations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Bypass this scope completely. Target ancestors, missing tails
                    // and outside-root exclusions must all retain their observations.
                    if (ReadObservation(component.Key) != component.Value)
                        throw new IOException($"A filesystem path component changed during directory membership capture: '{component.Key}'.");
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally { m_observations.Clear(); }
        }
    }
}
