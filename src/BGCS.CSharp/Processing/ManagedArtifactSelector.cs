using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Requests;

namespace BGCS.CSharp.Processing;

internal sealed class ManagedArtifactSelector
{
    private readonly ImmutableArray<ImmutableArray<string>> m_patterns;
    internal ImmutableArray<string> patterns { get; }

    internal ManagedArtifactSelector(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        this.patterns = patterns.ToImmutableArray();
        if (this.patterns.IsDefaultOrEmpty || this.patterns.Length > 64) throw new ArgumentException("A source processor requires between one and 64 managed-relative selectors.", nameof(patterns));
        if (this.patterns.Distinct(StringComparer.Ordinal).Count() != this.patterns.Length) throw new ArgumentException("Source path selectors must be unique.", nameof(patterns));
        m_patterns = this.patterns.Select(Compile).ToImmutableArray();
    }

    internal IEnumerable<ArtifactDescriptor> Select(
        ArtifactProcessingRequest request,
        CancellationToken cancellationToken
    ) {
        string prefix = Prefix(request);
        return request.artifacts.Where(artifact =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return artifact.mediaType == "text/x-csharp"
            && artifact.relativePath.StartsWith(prefix, StringComparison.Ordinal)
            && m_patterns.Any(pattern => Matches(pattern, artifact.relativePath[prefix.Length..], cancellationToken));
        })
            .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal);
    }

    internal static string Prefix(ArtifactProcessingRequest request) => request.api.interop.targetId + "/managed/";

    internal static void ValidatePath(
        string path,
        bool pattern
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > 1024 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl)
            || path.Split('/').Any(segment => segment is "" or "." or "..")
            || !pattern && path.IndexOfAny(['*', '?']) >= 0)
            throw new ArgumentException("A processor path must be a bounded portable managed-relative path without traversal.", nameof(path));
    }

    private static ImmutableArray<string> Compile(string path)
    {
        ValidatePath(path, pattern: true);
        ImmutableArray<string> segments = [.. path.Split('/')];
        foreach (string segment in segments)
            if (segment != "**" && segment.Contains("**", StringComparison.Ordinal))
                throw new ArgumentException("A recursive source selector must use ** as a complete path segment.", nameof(path));
        return segments;
    }

    private static bool Matches(
        ImmutableArray<string> pattern,
        string path,
        CancellationToken cancellationToken
    ) {
        // Portable globs have bounded states; match their segments directly instead of
        // imposing a wall-clock regex deadline on short paths in a busy build process.
        string[] parts = path.Split('/');
        bool[] next = new bool[parts.Length + 1], current = new bool[parts.Length + 1];
        next[parts.Length] = true;
        for (int index = pattern.Length - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(current);
            string segment = pattern[index];
            bool last = index == pattern.Length - 1, suffixWithoutNewline = true;
            for (int part = parts.Length - 1; part >= 0; part--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segment == "**")
                {
                    suffixWithoutNewline &= !parts[part].Contains('\n');
                    current[part] = last ? suffixWithoutNewline
                        : next[part] || part < parts.Length - 1 && parts[part].Length != 0 && current[part + 1];
                }
                else current[part] = next[part + 1]
                    && (last ? part == parts.Length - 1 : part < parts.Length - 1)
                    && (segment == "*" && parts[part].Length == 0
                        || FileSystemName.MatchesSimpleExpression(segment, parts[part], ignoreCase: false));
            }
            (next, current) = (current, next);
        }
        return next[0];
    }
}
