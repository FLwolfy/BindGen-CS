using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Processing;

/// <summary>Provides an advanced SDK Roslyn transformation; unfingerprinted subclasses disable affected generation caching.</summary>
public abstract class SyntaxArtifactProcessor : IArtifactProcessor
{
    private readonly ManagedArtifactSelector m_selector;

    /// <summary>Freezes portable managed candidate selectors; service identity belongs to its registration descriptor.</summary>
    /// <param name="paths">Managed-relative path selectors.</param>
    protected SyntaxArtifactProcessor(
        IEnumerable<string> paths
    )
    {
        m_selector = new(paths);
    }

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
