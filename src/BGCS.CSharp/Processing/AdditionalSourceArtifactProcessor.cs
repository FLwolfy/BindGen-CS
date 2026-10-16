using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Processing;

/// <summary>Adds one parsed C# candidate through the artifact owner before final checking and single-file composition.</summary>
public sealed class AdditionalSourceArtifactProcessor : IArtifactProcessor, ICacheFingerprintProvider
{
    private readonly string m_path;
    private readonly string m_content;

    /// <summary>Freezes a portable managed-relative path and complete source text.</summary>
    /// <param name="id">The unique processor identity.</param>
    /// <param name="priority">Descending execution priority, followed by ordinal identity.</param>
    /// <param name="path">A new relative C# path inside the managed candidate root.</param>
    /// <param name="content">Complete regular C# source; top-level executable statements are not allowed.</param>
    public AdditionalSourceArtifactProcessor(
        string id,
        int priority,
        string path,
        string content
    ) {
        SourceProcessingSupport.Identity(id); ManagedArtifactSelector.ValidatePath(path, pattern: false);
        ArgumentNullException.ThrowIfNull(content);
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || content.Length > 4 * 1024 * 1024)
            throw new ArgumentException("An added source requires a C# filename and at most 4 MiB of declared text.");
        ValidateSyntax(content, path, CancellationToken.None);
        this.id = id; this.priority = priority; m_path = path; m_content = content;
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
        cancellationToken.ThrowIfCancellationRequested();
        string path = ManagedArtifactSelector.Prefix(request) + m_path;
        if (request.artifacts.Any(artifact => string.Equals(artifact.relativePath, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An additional source collides with an existing candidate path: " + m_path);
        await request.content.AddTextAsync(path, "text/x-csharp", m_content, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return SourceProcessingSupport.Fingerprint(new { revision = 1, kind = "addSource", id, priority, path = m_path, content = m_content });
    }

    internal static void ValidateSyntax(
        string text,
        string path,
        CancellationToken cancellationToken
    ) {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp14), path, cancellationToken: cancellationToken);
        if (tree.GetRoot(cancellationToken).DescendantNodes().OfType<GlobalStatementSyntax>().Any()
            || tree.GetDiagnostics(cancellationToken).Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            throw new ArgumentException("A processed source must be valid regular library C# syntax without top-level statements: " + path);
    }
}
