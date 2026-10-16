using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Processing;

internal sealed class ManagedArtifactSelector
{
    private readonly ImmutableArray<Regex> m_patterns;
    internal ImmutableArray<string> patterns { get; }

    internal ManagedArtifactSelector(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        this.patterns = patterns.ToImmutableArray();
        if (this.patterns.IsDefaultOrEmpty || this.patterns.Length > 64) throw new ArgumentException("A source processor requires between one and 64 managed-relative selectors.", nameof(patterns));
        if (this.patterns.Distinct(StringComparer.Ordinal).Count() != this.patterns.Length) throw new ArgumentException("Source path selectors must be unique.", nameof(patterns));
        m_patterns = this.patterns.Select(Compile).ToImmutableArray();
    }

    internal IEnumerable<ArtifactDescriptor> Select(ArtifactProcessingRequest request)
    {
        string prefix = Prefix(request);
        return request.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp"
            && artifact.relativePath.StartsWith(prefix, StringComparison.Ordinal)
            && m_patterns.Any(pattern => pattern.IsMatch(artifact.relativePath[prefix.Length..])))
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

    private static Regex Compile(string path)
    {
        ValidatePath(path, pattern: true);
        StringBuilder expression = new("\\A");
        string[] segments = path.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            if (segment == "**") expression.Append(index == segments.Length - 1 ? ".*" : "(?:[^/]+/)*");
            else
            {
                if (segment.Contains("**", StringComparison.Ordinal)) throw new ArgumentException("A recursive source selector must use ** as a complete path segment.", nameof(path));
                foreach (char character in segment)
                    expression.Append(character switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(character.ToString()) });
                if (index != segments.Length - 1) expression.Append('/');
            }
        }
        expression.Append("\\z");
        return new(expression.ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    }
}
