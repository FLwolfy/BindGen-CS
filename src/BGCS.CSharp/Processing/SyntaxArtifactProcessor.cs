using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Processing;

/// <summary>Provides an advanced SDK Roslyn transformation; unfingerprinted subclasses disable affected generation caching.</summary>
public abstract class SyntaxArtifactProcessor : IArtifactProcessor
{
    private readonly ManagedArtifactSelector m_selector;

    /// <summary>Freezes transformation identity and portable managed candidate selectors.</summary>
    /// <param name="id">The globally unique processor identity.</param>
    /// <param name="priority">Descending priority followed by ordinal identity.</param>
    /// <param name="paths">Managed-relative path selectors.</param>
    protected SyntaxArtifactProcessor(
        string id,
        int priority,
        IEnumerable<string> paths
    ) {
        SourceProcessingSupport.Identity(id);
        this.id = id; this.priority = priority; m_selector = new(paths);
    }

    /// <inheritdoc/>
    public string id { get; }
    /// <inheritdoc/>
    public int priority { get; }

    /// <summary>Transforms one temporary parsed source; final ABI and candidate compilation checks remain authoritative.</summary>
    /// <param name="path">The managed-relative candidate path.</param>
    /// <param name="root">The detached C# compilation unit.</param>
    /// <param name="cancellationToken">Cancels cooperative syntax processing.</param>
    /// <returns>The complete replacement unit; null is rejected.</returns>
    protected abstract CompilationUnitSyntax Rewrite(
        string path,
        CompilationUnitSyntax root,
        CancellationToken cancellationToken
    );

    /// <inheritdoc/>
    public async ValueTask ProcessAsync(
        ArtifactProcessingRequest request,
        CancellationToken cancellationToken
    ) {
        string prefix = ManagedArtifactSelector.Prefix(request);
        foreach (var artifact in m_selector.Select(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken).ConfigureAwait(false);
            if (input.Length > SourceProcessingSupport.C_MAXIMUM_TEXT) throw new InvalidOperationException("A syntax processor input exceeds the candidate bound.");
            var tree = CSharpSyntaxTree.ParseText(input, new CSharpParseOptions(LanguageVersion.CSharp14), artifact.relativePath, cancellationToken: cancellationToken);
            CompilationUnitSyntax changed = Rewrite(artifact.relativePath[prefix.Length..], (CompilationUnitSyntax)await tree.GetRootAsync(cancellationToken).ConfigureAwait(false), cancellationToken)
                ?? throw new InvalidOperationException("A syntax processor returned no compilation unit.");
            string output = changed.ToFullString();
            if (output.Length > SourceProcessingSupport.C_MAXIMUM_TEXT) throw new InvalidOperationException("A syntax processor output exceeds the candidate bound.");
            AdditionalSourceArtifactProcessor.ValidateSyntax(output, artifact.relativePath, cancellationToken);
            await request.content.ReplaceTextAsync(artifact.relativePath, output, cancellationToken).ConfigureAwait(false);
        }
    }
}
