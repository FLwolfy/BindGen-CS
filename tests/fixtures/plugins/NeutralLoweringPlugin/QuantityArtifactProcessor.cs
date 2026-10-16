using System;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Annotates declared managed candidates through the neutral read and edit surface.</summary>
public sealed class QuantityArtifactProcessor : IArtifactProcessor, IDisposable
{
    /// <inheritdoc/>
    public string id => "fixture.quantity-processing";
    /// <inheritdoc/>
    public int priority => 0;
    /// <summary>Gets whether the processor has released its session resources.</summary>
    public bool disposed { get; private set; }
    /// <inheritdoc/>
    public async ValueTask ProcessAsync(ArtifactProcessingRequest request, CancellationToken cancellationToken)
    {
        foreach (var artifact in request.artifacts)
        {
            string text = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken).ConfigureAwait(false);
            await request.content.ReplaceTextAsync(artifact.relativePath, text + "\n// Checked domain extension candidate.\n", cancellationToken).ConfigureAwait(false);
        }
    }
    /// <inheritdoc/>
    public void Dispose() => disposed = true;
}
