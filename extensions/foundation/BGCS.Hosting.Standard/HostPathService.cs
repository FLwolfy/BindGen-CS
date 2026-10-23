using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Linq;
using BGCS.Core.Hosting;
using BGCS.Infrastructure.IO;

namespace BGCS.Hosting.Standard;

/// <summary>Compares each physical name under the directory that owns that component.</summary>
public sealed class HostPathService : IHostPathService
{
    private readonly Func<string, HostPathSemantics> m_observe;
    private readonly Func<Exception, HostIoFailureKind> m_classify;
    private readonly ConcurrentDictionary<string, HostPathSemantics> m_observations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> m_identityKeys = new(StringComparer.Ordinal);
    /// <summary>Creates a host path service from an explicit observed-directory strategy.</summary>
    /// <param name="observe">A read-only directory semantics observer.</param>
    /// <param name="classify">The host's explicit filesystem failure classifier.</param>
    public HostPathService(
        Func<string, HostPathSemantics> observe,
        Func<Exception, HostIoFailureKind> classify
    ) {
        m_observe = observe ?? throw new ArgumentNullException(nameof(observe));
        m_classify = classify ?? throw new ArgumentNullException(nameof(classify));
        comparer = new PathComparer(this);
    }
    /// <summary>Gets a component-aware path equality comparer.</summary>
    public IEqualityComparer<string> comparer { get; }
    /// <summary>Resolves the physical path and captures its host name comparison key.</summary>
    /// <param name="path">The host path.</param>
    /// <returns>The logical and physical path identities.</returns>
    public HostPathIdentity GetIdentity(string path)
    {
        string logical = Path.GetFullPath(path);
        string physical = PhysicalPathResolver.Resolve(logical, this);
        return new(logical, physical, GetIdentityKey(physical));
    }
    /// <summary>Observes directory semantics using its nearest existing directory when the tail is absent.</summary>
    /// <param name="directory">The directory whose child names are compared.</param>
    /// <returns>The currently observed comparison semantics.</returns>
    public HostPathSemantics GetSemantics(string directory)
    {
        string requested = Path.GetFullPath(directory);
        if (m_observations.TryGetValue(requested, out HostPathSemantics? captured)) return captured;
        // Freeze the requested directory name as well as existing owners. A formerly
        // missing directory must not change dictionary hash codes when it appears;
        // Verify independently rejects different semantics after creation.
        return m_observations.GetOrAdd(requested, _ => ObservePhysicalOwner(requested));
    }
    /// <summary>Preserves case for components owned by case-sensitive directories.</summary>
    /// <param name="path">The host path.</param>
    /// <returns>The path's ordinal name comparison key.</returns>
    public string GetIdentityKey(string path)
        => GetAbsoluteIdentityKey(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
    private string GetAbsoluteIdentityKey(string absolute)
    {
        if (m_identityKeys.TryGetValue(absolute, out string? captured)) return captured;
        // These keys derive only from the directory facts already frozen by
        // GetSemantics. Verify still reobserves every fact independently.
        return m_identityKeys.GetOrAdd(absolute, CreateIdentityKey);
    }
    private string CreateIdentityKey(string absolute)
    {
        string root = Path.GetPathRoot(absolute) ?? throw new ArgumentException("The host path has no root.", nameof(absolute));
        StringBuilder key = new(root.ToUpperInvariant());
        string directory = root;
        foreach (string component in absolute[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            if (key.Length != 0 && key[^1] != Path.DirectorySeparatorChar) key.Append(Path.DirectorySeparatorChar);
            key.Append(GetSemantics(directory).caseSensitive ? component : component.ToUpperInvariant());
            directory = Path.Combine(directory, component);
        }
        return key.ToString();
    }
    /// <summary>Compares two names according to the directories that own their components.</summary>
    /// <param name="left">The first host path.</param>
    /// <param name="right">The second host path.</param>
    /// <returns>Whether the two name identities match.</returns>
    public bool Equals(
        string left,
        string right
    ) => string.Equals(GetIdentityKey(left), GetIdentityKey(right), StringComparison.Ordinal);
    /// <summary>Checks the full component boundary after host-aware name normalization.</summary>
    /// <param name="candidate">The candidate path.</param>
    /// <param name="root">The owner path.</param>
    /// <param name="includeRoot">Whether equality qualifies.</param>
    /// <returns>Whether the candidate is equal to or below the owner as requested.</returns>
    public bool IsWithin(
        string candidate,
        string root,
        bool includeRoot = true
    ) {
        string absoluteCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string absoluteRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        // Even maximally folding ASCII case cannot make unrelated names belong
        // to this root. Possible matches and all Unicode names still require the
        // complete observed component rules below.
        if (IsAscii(absoluteCandidate) && IsAscii(absoluteRoot)
            && !string.Equals(absoluteCandidate, absoluteRoot, StringComparison.OrdinalIgnoreCase)
            && !absoluteCandidate.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        string child = GetAbsoluteIdentityKey(absoluteCandidate), parent = GetAbsoluteIdentityKey(absoluteRoot);
        return includeRoot && string.Equals(child, parent, StringComparison.Ordinal)
            || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    private static bool IsAscii(string value)
    {
        foreach (char character in value)
            if (character > 0x7f) return false;
        return true;
    }
    /// <summary>Computes a relative path without folding components owned by case-sensitive directories.</summary>
    /// <param name="baseDirectory">The starting directory.</param>
    /// <param name="path">The destination.</param>
    /// <returns>The observed-semantics relative path, or an absolute destination on a different root.</returns>
    public string GetRelativePath(
        string baseDirectory,
        string path
    ) {
        string origin = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(origin)!;
        if (!Equals(root, Path.GetPathRoot(destination)!)) return destination;
        string[] left = origin[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        string[] right = destination[Path.GetPathRoot(destination)!.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        string directory = root;
        while (common < left.Length && common < right.Length && GetSemantics(directory).comparer.Equals(left[common], right[common]))
        {
            directory = Path.Combine(directory, left[common]);
            common++;
        }
        string[] components = Enumerable.Repeat("..", left.Length - common).Concat(right.Skip(common)).ToArray();
        return components.Length == 0 ? "." : Path.Combine(components);
    }
    /// <summary>Classifies a failure using the explicit host operation strategy.</summary>
    /// <param name="failure">The original filesystem access failure.</param>
    /// <returns>The selected host's retry category.</returns>
    public HostIoFailureKind ClassifyIoFailure(Exception failure) => m_classify(failure);
    /// <summary>Reobserves comparison facts for surviving directories without cache reuse.</summary>
    /// <remarks>Input and owner snapshots separately enforce existence and physical identity. An owned staging tree may be renamed during publication; its former missing path has no current comparison fact to read. Its frozen key remains stable for existing dictionaries.</remarks>
    public void Verify()
    {
        foreach (var observed in m_observations)
            if (Directory.Exists(observed.Key) && ObservePhysicalOwner(observed.Key) != observed.Value)
                throw new IOException("Host directory comparison semantics changed during the session: " + observed.Key);
    }
    private HostPathSemantics ObservePhysicalOwner(string directory)
    {
        string current = directory;
        while (!Directory.Exists(current))
            current = Path.GetDirectoryName(current) ?? throw new DirectoryNotFoundException("No existing directory establishes host path semantics: " + directory);
        try { return m_observe(current); }
        catch (IOException) when (new DirectoryInfo(current).LinkTarget is not null)
        {
            // Directory.Exists can report a dangling junction itself as present.
            // Normal observed directories keep their direct read-only fast path;
            // only a proven missing physical target uses its existing parent.
            string physical = PhysicalPathResolver.Resolve(current, this);
            bool missing = false;
            try { _ = File.GetAttributes(physical); }
            catch (FileNotFoundException) { missing = true; }
            catch (DirectoryNotFoundException) { missing = true; }
            if (!missing) throw;
            if (Path.GetDirectoryName(physical) is not string parent) throw;
            current = parent;
            return ObservePhysicalOwner(current);
        }
    }
    private sealed class PathComparer(HostPathService owner) : IEqualityComparer<string>
    {
        public bool Equals(
            string? left,
            string? right
        ) => left is null || right is null ? left is null && right is null : owner.Equals(left, right);
        public int GetHashCode(string path) => StringComparer.Ordinal.GetHashCode(owner.GetIdentityKey(path));
    }
}
