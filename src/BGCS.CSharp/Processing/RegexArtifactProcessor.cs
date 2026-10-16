using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.CSharp.Processing;

/// <summary>Applies invariant, explicitly timed .NET regex substitutions to declared managed candidates.</summary>
public sealed class RegexArtifactProcessor : IArtifactProcessor, ICacheFingerprintProvider
{
    private readonly ManagedArtifactSelector m_selector;
    private readonly Regex m_expression;
    private readonly string m_replacement;
    private readonly bool m_requireMatch;
    private readonly HashSet<string> m_groups;
    private readonly int m_lastGroup;

    /// <summary>Freezes the regex, replacement, selector and finite per-artifact execution budget.</summary>
    /// <param name="id">The unique processor identity.</param>
    /// <param name="priority">Descending pipeline priority, followed by ordinal identity.</param>
    /// <param name="paths">Portable managed-relative candidate selectors.</param>
    /// <param name="pattern">The bounded .NET expression.</param>
    /// <param name="replacement">Literal text and standard .NET dollar substitutions.</param>
    /// <param name="timeoutMilliseconds">A positive per-artifact total and matching budget, at most 30000 milliseconds.</param>
    /// <param name="options">Supported matching options; invariant culture is always applied and right-to-left matching is rejected.</param>
    /// <param name="requireMatch">Requires an actual match across the selected candidates.</param>
    public RegexArtifactProcessor(
        string id,
        int priority,
        IEnumerable<string> paths,
        string pattern,
        string replacement,
        int timeoutMilliseconds = 1000,
        RegexOptions options = RegexOptions.None,
        bool requireMatch = true
    ) {
        SourceProcessingSupport.Identity(id);
        ArgumentException.ThrowIfNullOrEmpty(pattern); ArgumentNullException.ThrowIfNull(replacement);
        const RegexOptions allowed = RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.ExplicitCapture
            | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.NonBacktracking;
        if ((options & ~allowed) != 0 || timeoutMilliseconds is < 1 or > 30000 || pattern.Length > 65536 || replacement.Length > 1048576)
            throw new ArgumentException("A source regex requires supported invariant options, bounded text and a finite 1..30000 millisecond budget.");
        this.id = id; this.priority = priority;
        m_selector = new(paths); m_replacement = replacement; m_requireMatch = requireMatch;
        m_expression = new(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(timeoutMilliseconds));
        m_groups = m_expression.GetGroupNames().Concat(m_expression.GetGroupNumbers()
            .Select(number => number.ToString(CultureInfo.InvariantCulture))).ToHashSet(StringComparer.Ordinal);
        m_lastGroup = m_expression.GetGroupNumbers()[^1];
    }

    /// <inheritdoc/>
    public string id { get; }
    /// <inheritdoc/>
    public int priority { get; }

    /// <inheritdoc/>
    public async ValueTask ProcessAsync(
        ArtifactProcessingRequest request,
        CancellationToken cancellationToken
    ) {
        bool matched = false;
        foreach (var artifact in m_selector.Select(request))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken).ConfigureAwait(false);
            if (input.Length > SourceProcessingSupport.C_MAXIMUM_TEXT) throw new InvalidOperationException("A source artifact exceeds the processor input bound.");
            StringBuilder output = new();
            int start = 0;
            long began = Stopwatch.GetTimestamp();
            for (Match match = m_expression.Match(input); match.Success; match = match.NextMatch())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(began) > m_expression.MatchTimeout)
                    throw new RegexMatchTimeoutException("The complete source regex pass exceeded its declared budget.");
                matched = true;
                SourceProcessingSupport.Append(output, input.AsSpan(start, match.Index - start));
                SourceProcessingSupport.Replacement(output, m_replacement, match, input, m_groups, m_lastGroup);
                start = match.Index + match.Length;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(began) > m_expression.MatchTimeout)
                throw new RegexMatchTimeoutException("The complete source regex pass exceeded its declared budget.");
            SourceProcessingSupport.Append(output, input.AsSpan(start));
            if (start != 0 || output.Length != input.Length)
                await request.content.ReplaceTextAsync(artifact.relativePath, output.ToString(), cancellationToken).ConfigureAwait(false);
        }
        if (m_requireMatch && !matched) throw new InvalidOperationException("The regex source processor matched no declared text: " + id);
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return SourceProcessingSupport.Fingerprint(new
        {
            revision = 1,
            kind = "regex",
            id,
            priority,
            m_selector.patterns,
            pattern = m_expression.ToString(),
            replacement = m_replacement,
            options = (int)m_expression.Options,
            timeoutTicks = m_expression.MatchTimeout.Ticks,
            requireMatch = m_requireMatch
        });
    }
}
