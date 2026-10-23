using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.CSharp.Processing;

/// <summary>Replaces literal managed candidate text with a frozen, ordinal and completely fingerprinted rule.</summary>
public sealed class TextArtifactProcessor : IArtifactProcessor, ICacheFingerprintProvider
{
    private readonly ManagedArtifactSelector m_selector;
    private readonly string m_match;
    private readonly string m_replacement;
    private readonly bool m_ignoreCase;
    private readonly bool m_requireMatch;

    /// <summary>Freezes a literal rule without reading formal output or accepting executable configuration.</summary>
    /// <param name="paths">Portable managed-relative selectors, using *, ? and complete ** segments.</param>
    /// <param name="match">The nonempty literal text to replace.</param>
    /// <param name="replacement">The literal replacement, without regex substitution.</param>
    /// <param name="ignoreCase">Selects ordinal case-insensitive matching.</param>
    /// <param name="requireMatch">Requires at least one actual text match across the selected artifacts.</param>
    public TextArtifactProcessor(
        IEnumerable<string> paths,
        string match,
        string replacement,
        bool ignoreCase = false,
        bool requireMatch = true
    ) {
        ArgumentException.ThrowIfNullOrEmpty(match);
        ArgumentNullException.ThrowIfNull(replacement);
        if (match.Length > 65536 || replacement.Length > 1048576) throw new ArgumentException("A source text rule exceeds its bounded declaration size.");
        m_selector = new(paths); m_match = match; m_replacement = replacement; m_ignoreCase = ignoreCase; m_requireMatch = requireMatch;
    }

    /// <inheritdoc/>
    public async ValueTask ProcessAsync(
        ArtifactProcessingRequest request,
        CancellationToken cancellationToken
    ) {
        bool matched = false;
        foreach (var artifact in m_selector.Select(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken).ConfigureAwait(false);
            if (input.Length > SourceProcessingSupport.C_MAXIMUM_TEXT) throw new InvalidOperationException("A source artifact exceeds the processor input bound.");
            string output = SourceProcessingSupport.Literal(input, m_match, m_replacement,
                m_ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, cancellationToken, out bool changed);
            matched |= changed;
            if (changed) await request.content.ReplaceTextAsync(artifact.relativePath, output, cancellationToken).ConfigureAwait(false);
        }
        if (m_requireMatch && !matched) throw new InvalidOperationException("The literal source processor matched no declared text.");
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return SourceProcessingSupport.Fingerprint(new
        {
            revision = 2,
            kind = "literal",
            m_selector.patterns,
            match = m_match,
            replacement = m_replacement,
            ignoreCase = m_ignoreCase,
            requireMatch = m_requireMatch
        });
    }
}
